using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

public partial class Bugcord : Node
{
	[Export] public TextEdit messageInput;
	[Export] public Control registerWindow;

	[Export] public SpacesList spacesList;

	[Export] public ServerSelector serverSelector;

	[Export] public AudioStreamPlayer audioRecorder;
	[Export] public AudioStreamPlayer audioPlayer;

	[Export] public CheckBox voiceChatToggle;

	[Signal] public delegate void OnMessageRecievedEventHandler(Dictionary message);
	[Signal] public delegate void OnEmbedMessageRecievedEventHandler(Dictionary message);
	[Signal] public delegate void OnLoggedInEventHandler(Dictionary client);
	[Signal] public delegate void OnEmbedCachedEventHandler(string id);

	public const string clientSavePath = "user://client.data";
	public const string clientKeyPath = "user://client.auth";
	public const string clientPeerPath = "user://peers.json";
	public const string clientSpacesPath = "user://spaces.json";

	public const string knownKeysPath = "user://keys.auth";

	public const string cachePath = "user://cache/";
	public const string dataServePath = "user://serve/";

	public const int minAudioFrames = 2048;
	public const int maxAudioFrames = 4096;

	public const int defaultPort = 25987;

	public static Dictionary clientUser;

	public static Dictionary peers;
	public static Dictionary spaces;

	public static Dictionary aesKeys;

	public static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, List<byte[]>>> incomingFileStaging = new();
	public static Dictionary cacheIndex;

	public static string selectedSpaceId;
	public static string selectedKeyId;

	private static RSA clientAuth;
	private static StreamPeerTcp tcpClient;
	private static PacketPeerUdp udpClient;

	private static StreamPeerTcp.Status previousState;

	private static Bugcord instance;

	private static AudioEffectCapture recordBusCapture;
	private static AudioStreamGeneratorPlayback voicePlaybackBus;

	private static List<Vector2> currentFrameAudio = new List<Vector2>();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		instance = this;

		registerWindow.Visible = false;

		if (!LogIn()){
			registerWindow.Visible = true;
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (tcpClient == null || clientAuth == null){
			return;
		}
		
		tcpClient.Poll();

		StreamPeerTcp.Status clientStatus = tcpClient.GetStatus();
		if (clientStatus != StreamPeerTcp.Status.Connected){
			return;
		}

		if (clientStatus == StreamPeerTcp.Status.Connected && previousState != StreamPeerTcp.Status.Connected){
			OnConnected();
		}

		previousState = clientStatus;

		if (tcpClient.GetAvailableBytes() >= 8){ // 8 Bytes is the absolute minimum size for a packet
			Godot.Collections.Array recieved = tcpClient.GetPartialData(65535);
			byte[] rawPacket = (byte[])recieved[1];
			ProcessRawPacket(rawPacket);
		}

		if (!udpClient.IsSocketConnected()){
			return;
		}

		while (udpClient.GetAvailablePacketCount() > 0){
			ProcessIncomingPacket(udpClient.GetPacket());
		}
		
		voicePlaybackBus.PushBuffer(currentFrameAudio.ToArray());
		currentFrameAudio.Clear();

		if (voiceChatToggle.ButtonPressed)
			Send(BuildVoicePacket(GetVoiceBuffer()), true);
	}

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest){
			if (tcpClient != null)
				tcpClient.DisconnectFromHost();
			GetTree().Quit();
		}
    }

	#region Voice Chat

	public void InitVoice(){
		AudioStreamMicrophone audioStreamMicrophone = new AudioStreamMicrophone();
		audioRecorder.Stream = audioStreamMicrophone;
		audioRecorder.Play();

		int recordBusIndex = AudioServer.GetBusIndex("Record");
		
		recordBusCapture = (AudioEffectCapture)AudioServer.GetBusEffect(recordBusIndex, 0);
		voicePlaybackBus = (AudioStreamGeneratorPlayback)audioPlayer.GetStreamPlayback();
	}

	public Vector2[] GetVoiceBuffer(){
		int framesAvailable = recordBusCapture.GetFramesAvailable();
		
		if (framesAvailable >= minAudioFrames){
			if (framesAvailable <= maxAudioFrames)
				return recordBusCapture.GetBuffer(framesAvailable);

			// to many frames available
			Vector2[] frames = recordBusCapture.GetBuffer(maxAudioFrames);
			recordBusCapture.ClearBuffer();
			return frames;
		}
		
		return new Vector2[0];
	}

	#endregion

    #region server client

    // prepares a file to be served and sends an embed linked message
    public void SubmitEmbed(string directory){
		string guidString = Guid.NewGuid().ToString();

		PrepareEmbed(directory, guidString);

		Send(BuildEmbedMessage(guidString));
	}

	// encrypts a copy of the file with its uuid, init vector, and true filename stored as a dataspan at the start
	// places the plaintext version in cache (does not include filename or uuid dataspan)
	// encrypted version is placed in client's servable folder with the filename of the file's uuid
	public static void PrepareEmbed(string directory, string guid, bool encrypted){
		FileAccess embedFile = FileAccess.Open(directory, FileAccess.ModeFlags.Read);
		
		byte[] embedData = embedFile.GetBuffer((long)embedFile.GetLength());
		GD.Print(embedData.Length);
		string filename = System.IO.Path.GetFileName(directory);

		GD.Print("preparing embedded file " + filename);

		byte[] fileGuid = guid.ToUtf8Buffer();
		List<byte> serveCopyData = new List<byte>();

		if (encrypted){
			byte[] iv = GetRandomBytes(16);
			byte[] encryptedData = AESEncrypt(embedData, GetSpaceKey(selectedSpaceId), iv);

			serveCopyData.Add(0);

			serveCopyData.AddRange(MakeDataSpan(fileGuid));
			serveCopyData.AddRange(MakeDataSpan(iv));
			serveCopyData.AddRange(MakeDataSpan(selectedKeyId.ToUtf8Buffer()));
			serveCopyData.AddRange(MakeDataSpan(filename.ToUtf8Buffer()));
			serveCopyData.AddRange(MakeDataSpan(encryptedData, 0)); // cant use dataspans for this since the files length in bytes may be more than 2^16
		}else{
			serveCopyData.Add(1); // indicate no encryption

			serveCopyData.AddRange(MakeDataSpan(fileGuid));
			serveCopyData.AddRange(MakeDataSpan(filename.ToUtf8Buffer()));
			serveCopyData.AddRange(MakeDataSpan(embedData, 0));
		}

		WriteToCache(embedData, filename, guid);
		WriteToServable(serveCopyData.ToArray(), guid);
	}

	public static void PrepareEmbed(string directory, string guid){
		PrepareEmbed(directory, guid, true);
	}

	public bool CacheServedFile(byte[] file){
		byte[][] dataSpans = ReadDataSpans(file, 1);
		byte flags = file[0];

		switch (flags)
		{
			case 0: // Encrypted
				string filename = dataSpans[3].GetStringFromUtf8();
				string fileGuid = dataSpans[0].GetStringFromUtf8();

				string keyId = dataSpans[2].GetStringFromUtf8();

				if (!aesKeys.ContainsKey(keyId))
					return false;

				byte[] key = (byte[])aesKeys[keyId];
				byte[] iv = dataSpans[1];

				byte[] decryptedData = AESDecrypt(dataSpans[4], key, iv);

				WriteToCache(decryptedData, filename, fileGuid);

				break;
			case 1: // Not encrypted file
				filename = dataSpans[1].GetStringFromUtf8();
				fileGuid = dataSpans[0].GetStringFromUtf8();

				WriteToCache(dataSpans[2], filename, fileGuid);

				break;
		}

		return true;
	}

	private static void WriteToServable(byte[] data, string guid){
		if (!DirAccess.DirExistsAbsolute(dataServePath)){
			DirAccess cacheDir = DirAccess.Open("user://");
			cacheDir.MakeDir("serve");
		}

		FileAccess serveCopy = FileAccess.Open(dataServePath + guid + ".file", FileAccess.ModeFlags.Write);
		serveCopy.StoreBuffer(data);

		serveCopy.Close();
	}

	private static void WriteToCache(byte[] data, string filename, string guid){
		if (!DirAccess.DirExistsAbsolute(cachePath)){
			DirAccess cacheDir = DirAccess.Open("user://");
			cacheDir.MakeDir("cache");
		}

		string path = cachePath + filename;

		GD.Print("writing to cache " + path);

		FileAccess cacheCopy = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		cacheCopy.StoreBuffer(data);

		cacheCopy.Close();

		cacheIndex.Add(guid, path);

		instance.EmitSignal(SignalName.OnEmbedCached, guid);
	}

	private byte[] GetServableData(string guid){
		FileAccess file = FileAccess.Open(dataServePath + guid + ".file", FileAccess.ModeFlags.Read);
		return file.GetBuffer((long)file.GetLength());
	}

	private bool HasServableFile(string guid){
		FileAccess file = FileAccess.Open(dataServePath + guid + ".file", FileAccess.ModeFlags.Read);
		if (file == null)
			return false;
		return true;
	}

	#endregion

	#region message functions

	public void DisplayMessage(string content, string senderId){
		Dictionary messageDict = new Dictionary
		{
			{"content", content},
			{"sender", ((Dictionary)peers[senderId])["username"]}
		};

		EmitSignal(SignalName.OnMessageRecieved, messageDict);
	}

	public void DisplayMediaMessage(string mediaId, string senderId){
		GD.Print("displaying media message " + mediaId);

		Dictionary messageDict = new Dictionary
		{
			{"mediaId", mediaId},
			{"sender", ((Dictionary)peers[senderId])["username"]}
		};

		EmitSignal(SignalName.OnEmbedMessageRecieved, messageDict);
	}

	public void PostMessage(string message){
		// client.SendText(message);
		Send(BuildMsgPacket(message));
	}

	#endregion

	#region connection & websocket interaction

	public void Connect(string url){
		if (clientAuth == null){
			GD.Print("no auth");
			return;
		}

		clientUser["defaultConnectServer"] = url;
		SaveUser();

		string[] urlSplit = url.Split(":");
		string urlHost = urlSplit[0];
		int urlPort = defaultPort;
		if (urlSplit.Length > 1){
			urlPort = int.Parse(urlSplit[1]);
		}
		
		GD.Print("connecting..");
		tcpClient = new StreamPeerTcp();
		tcpClient.ConnectToHost(urlHost, urlPort);

		udpClient = new PacketPeerUdp();
		udpClient.ConnectToHost(urlHost, urlPort);
	}

	public void Send(byte[] data){
		Send(data, false);
	}

	public void Send(byte[] data, bool sendUnrelyable){
		if (data.Length == 0)
			return;

		GD.Print("Sending message. Type: " + data[0]);

		if (sendUnrelyable){
			udpClient.PutPacket(data);
		}else{
			tcpClient.PutData(BuildMasterPacket(data));
		}
	}

	public void SetAutoConnect(bool setTrue){
		if (setTrue){
			clientUser["autoConnectToServer"] = "true";
		}else{
			clientUser["autoConnectToServer"] = "false";
		}

		SaveUser();
	}

	public void OnConnected(){
		GD.Print("connected");
		Send(BuildIdentifyingPacket());
		InitVoice();
	}

	#endregion

	#region user file related

	public bool LogIn(){
		if (!FileAccess.FileExists(clientSavePath)){
			return false;
		}
		if (!FileAccess.FileExists(clientKeyPath)){
			return false;
		}

		if (!FileAccess.FileExists(clientPeerPath)){
			MakeNewPeerFile();
		}
		if (!FileAccess.FileExists(clientSpacesPath)){
			MakeNewSpaceFile();
		}
		if (!FileAccess.FileExists(knownKeysPath)){
			MakeNewAesKeyFile();
		}

		LoadUser();

		// User RSA key
		LoadPersonalKey();

		LoadPeers();

		LoadSpaces();

		LoadKeys();

		EmitSignal(SignalName.OnLoggedIn, clientUser);

		cacheIndex = new Dictionary();

		if ((string)clientUser["autoConnectToServer"] == "true"){
			Connect((string)clientUser["defaultConnectServer"]);
		}

		return true;
	}

	public void CreateUserFile(string username, string password){
		GD.Print("creating new user.. " + username);

		clientUser = new Dictionary
		{
			{"id", Guid.NewGuid().ToString()},
			{ "username", username },
			{"defaultConnectServer", "75.71.255.149:25987"},
			{"autoConnectToServer", "false"}
		};
		SaveUser();

		clientAuth = new RSACryptoServiceProvider(2048);
		SavePersonalKey();

		MakeNewPeerFile();
		MakeNewSpaceFile();

		LogIn();
	}

	private void MakeNewPeerFile(){
		FileAccess peerList = FileAccess.Open(clientPeerPath, FileAccess.ModeFlags.Write);
		Godot.Collections.Dictionary<string, string> peerDict = new();
		peerList.StoreString(Json.Stringify(peerDict));
		peerList.Close();
	}

	private void MakeNewSpaceFile(){
		FileAccess spaceList = FileAccess.Open(clientSpacesPath, FileAccess.ModeFlags.Write);
		Godot.Collections.Dictionary<string, string> spaceDict = new();
		spaceList.StoreString(Json.Stringify(spaceDict));
		spaceList.Close();
	}

	private void MakeNewAesKeyFile(){
		FileAccess keyList = FileAccess.Open(knownKeysPath, FileAccess.ModeFlags.Write);
		Godot.Collections.Dictionary<string, string> keyDict = new();
		keyList.StoreString(Json.Stringify(keyDict));
		keyList.Close();
	}

	public string GetClientId(){
		return (string)clientUser["id"];
	}

	#endregion

	#region space functions

	public void GenerateSpace(string name){
		Aes spaceKey = Aes.Create();
		string keyGuid = Guid.NewGuid().ToString();

		Dictionary spaceData = new Dictionary(){
			{"name", name},
			{"keyId", keyGuid}
		};

		aesKeys.Add(keyGuid, spaceKey.Key);
		spaces.Add(Guid.NewGuid().ToString(), spaceData);
		SaveKeys();
		SaveSpaces();
	}

	public void ConnectSpace(string guid){
		selectedSpaceId = guid;
		selectedKeyId = GetSpaceKeyId(guid);

		GD.Print("connected to space " + guid);
		AlertPanel.PostAlert("Connected to space", guid);
	}

	public void SendSpaceInvite(string spaceGuid, string peerGuid){
		string recipiantKeyB64 = (string)((Dictionary)peers[peerGuid])["rsapublickey"];
		byte[] recipiantKey = FromBase64(recipiantKeyB64);;

		byte[] spaceInvitePacket = BuildSpaceInvite(recipiantKey, spaceGuid);

		Send(spaceInvitePacket);
	}

	private static Dictionary GetSpace(string spaceId){
		return (Dictionary)spaces[spaceId];
	}

	private static byte[] GetSpaceKey(string spaceId){
		return (byte[])aesKeys[GetSpaceKeyId(spaceId)];
	}

	private static string GetSpaceKeyId(string spaceId){
		Dictionary space = (Dictionary)spaces[spaceId];

		return (string)space["keyId"];
	}

	#endregion

	#region packet processors

	private void ProcessRawPacket(byte[] data){
		GD.Print("Attempting to process packet");
		int offset = 0;

		while (offset < data.Length - 8){
			int version = BitConverter.ToInt16(data, offset);
			short checksum = BitConverter.ToInt16(data, offset + 2);
			int length = BitConverter.ToInt16(data, offset + 4);

			if (length > data.Length - (6 + offset)){
				offset++;
				continue;
			}

			byte[] packet = ReadLength(data, offset + 6, length);

			if (!ValidateSumComplement(packet, (ushort)checksum)){
				offset++;
				continue;
			}

			GD.Print("Checksum verified. Offset: " + offset);
			ProcessIncomingPacket(packet);
			return;
		}

		GD.Print("Checksum could not be verified");
	}

	private void ProcessIncomingPacket(byte[] packet){
		byte type = packet[0];
		GD.Print("Recieved packet. Type: " + type);

		switch (type){
			case 0:
				ProcessMessagePacket(packet);
				break;
			case 1:
				ProcessIdentify(packet);
				break;
			case 4:
				ProcessSpaceInvite(packet);
				break;
			case 5:
				ProcessEmbedMessage(packet);
				break;
			case 6:
				ProcessFileRequest(packet);
				break;
			case 7:
				ProcessFilePacket(packet);
				break;
			case 8:
				ProcessVoicePacket(packet);
				break;
		}
	}

	private void ProcessVoicePacket(byte[] packet){
		byte[][] dataSpans = ReadDataSpans(packet, 1);

		byte[] framesEncoded = dataSpans[0];
		Vector2[] voiceFrames = new Vector2[framesEncoded.Length];

		for (int i = 0; i < framesEncoded.Length; i++){
			float f = ByteToFloat(framesEncoded[i]);

			voiceFrames[i] = new Vector2(f, f);
		}

		voicePlaybackBus.PushBuffer(voiceFrames);
	}

	private void ProcessFilePacket(byte[] packet){
		GD.Print("recieved file packet");

		byte[][] dataSpans = ReadDataSpans(packet, 5);

		ushort filePart = BitConverter.ToUInt16(packet, 1);
		ushort filePartMax = BitConverter.ToUInt16(packet, 3);

		string fileGuid = dataSpans[0].GetStringFromUtf8();
		string senderGuid = dataSpans[1].GetStringFromUtf8();
		byte[] fileData = dataSpans[2];

		// Is the file known?
		if (!incomingFileStaging.ContainsKey(fileGuid)){
            System.Collections.Generic.Dictionary<string, List<byte[]>> recievingFile = new();
            incomingFileStaging.Add(fileGuid, recievingFile);
		}

		// Has this user sent parts before?
		if (!incomingFileStaging[fileGuid].ContainsKey(senderGuid)){
			incomingFileStaging[fileGuid].Add(senderGuid, new List<byte[]>(filePartMax)); 
		}
	
		incomingFileStaging[fileGuid][senderGuid][filePart] = fileData;

		for (int i = 0; i < incomingFileStaging[fileGuid][senderGuid].Count; i++){
			if (incomingFileStaging[fileGuid][senderGuid][i] == null){
				return;
			}
		}

		// No parts are missing so concatinate everything and save and cache
		List<byte> fullFile = new();
		for (int i = 0; i < incomingFileStaging[fileGuid][senderGuid].Count; i++){
			fullFile.AddRange(incomingFileStaging[fileGuid][senderGuid][i]);
		}
		incomingFileStaging[fileGuid].Remove(senderGuid);

		WriteToServable(fullFile.ToArray(), fileGuid);
		CacheServedFile(fullFile.ToArray());
	}

	private void ProcessFileRequest(byte[] packet){
		GD.Print("Recieved file request");
		
		byte subtype = packet[1];
		string fileGuid = ReadDataSpan(packet, 2).GetStringFromUtf8();

		switch (subtype){
			case 0:
				if (!HasServableFile(fileGuid)) // stop if we dont have this file
					return;

				byte[][] servePartitions = MakePartitions(GetServableData(fileGuid), 64000);
				for(int i = 0; i < servePartitions.Length; i++){
					Send(BuildFilePacket(fileGuid, i, servePartitions.Length - 1, servePartitions[i]));
				}
				break;
		}
	}

	private void ProcessEmbedMessage(byte[] packet){
		GD.Print("Processing embedded message");

		byte[][] dataSpans = ReadDataSpans(packet, 1);

		string senderId = dataSpans[0].GetStringFromUtf8();
		string embedId = dataSpans[1].GetStringFromUtf8();

		DisplayMediaMessage(embedId, senderId);

		if (cacheIndex.ContainsKey(embedId)){
			EmitSignal(SignalName.OnEmbedCached, embedId); // Call the embed message ui to update and load the image from cache
			return;
		}

		Send(BuildFileRequest(embedId));
	}

	private void ProcessSpaceInvite(byte[] packet){
		GD.Print("Processing space invite");

		byte[][] dataSpans = ReadDataSpans(packet, 1);

		byte[] uuid = dataSpans[0];
		byte[] spaceName = dataSpans[1];
		byte[] keyId = dataSpans[2];
		byte[] encryptedSpaceKey = dataSpans[3];

		if (spaces.ContainsKey(uuid.GetStringFromUtf8())){
			GD.Print("client already in space");
			return;
		}

		byte[] spaceKey = new byte[32]; // Size of AES key in bytes. 256 bits = 32 bytes
		bool couldDecrypt = clientAuth.TryDecrypt(encryptedSpaceKey, spaceKey, RSAEncryptionPadding.Pkcs1, out int bytesWritten);

		if (couldDecrypt){
			Dictionary spaceData = new Dictionary(){
				{"name", spaceName.GetStringFromUtf8()},
				{"keyId", keyId.GetStringFromUtf8()}
			};
			
			aesKeys.Add(keyId.GetStringFromUtf8(), spaceKey);
			spaces.Add(uuid.GetStringFromUtf8(), spaceData);
			SaveKeys();
			SaveSpaces();
		}
	}

	private void ProcessIdentify(byte[] packet){
		byte[][] packetDataSpans = ReadDataSpans(packet, 1);

		string guid = packetDataSpans[0].GetStringFromUtf8();
		string username = packetDataSpans[1].GetStringFromUtf8();
		string key = ToBase64(packetDataSpans[2]);

		bool peerKnown = peers.ContainsKey(guid);
		if (peerKnown){
			GD.Print("peer already known");
			return;
		}else{
			GD.Print("adding peer");

			Dictionary newPeer = new Dictionary(){
				{"username", username},
				{"rsapublickey", key}
			};

			peers.Add(guid, newPeer);
			SavePeers();
			Send(BuildIdentifyingPacket());
		}
	}

	private void ProcessMessagePacket(byte[] packet){
		byte[][] spans = ReadDataSpans(packet, 17);

		byte[] initVector = ReadLength(packet, 1, 16);

		byte[] keyUsed = spans[0];
		byte[] encryptedMessage = spans[1];
		byte[] senderGuid = spans[2];

		byte[] decryptedMessage = null;

		using (Aes aes = Aes.Create()){
			aes.Key = (byte[])aesKeys[keyUsed.GetStringFromUtf8()];
			aes.IV = initVector;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;

			using (ICryptoTransform decryptor = aes.CreateDecryptor()){
				decryptedMessage = decryptor.TransformFinalBlock(encryptedMessage, 0, encryptedMessage.Length);
			}
		}
		
		string messageString = decryptedMessage.GetStringFromUtf8();
		if (messageString.Length > 0)
			DisplayMessage(messageString, senderGuid.GetStringFromUtf8());
	}

	#endregion

	#region packet builders

	private byte[] BuildMasterPacket(byte[] data){
		List<byte> packetBytes = new List<byte>{
			0, // Version
			0, // Version
		};

		byte[] checksum = GetChecksum(data);
		ushort packetLength = (ushort)data.Length;
		packetBytes.AddRange(checksum);
		packetBytes.AddRange(BitConverter.GetBytes(packetLength));
		packetBytes.AddRange(data);

		return packetBytes.ToArray();
	}

	private byte[] BuildVoicePacket(Vector2[] audioFrames){
		if (audioFrames.Length == 0)
			return new byte[0];

		List<byte> packetBytes = new List<byte>{
			8
		};

		byte[] codedFrames = new byte[audioFrames.Length];

		for (int i = 0; i < audioFrames.Length; i++){
			byte f = FloatToByte(audioFrames[i].X);

			codedFrames[i] = f;
		}

		packetBytes.AddRange(MakeDataSpan(codedFrames, 0));

		return packetBytes.ToArray();
	}

	private byte[] BuildFilePacket(string fileGuid, int filePart, int lastFilePartIndex, byte[] data){
		List<byte> packetBytes = new List<byte>{
			7
		};

		packetBytes.AddRange(BitConverter.GetBytes((ushort)filePart));
		packetBytes.AddRange(BitConverter.GetBytes((ushort)lastFilePartIndex));
		packetBytes.AddRange(MakeDataSpan(fileGuid.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(GetClientId().ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(data, 0));

		return packetBytes.ToArray();
	}

	private byte[] BuildFileRequest(string fileGuid){
		List<byte> packetBytes = new List<byte>{
			6,
			0 // request subtype
		};

		packetBytes.AddRange(MakeDataSpan(fileGuid.ToUtf8Buffer()));

		return packetBytes.ToArray();
	}

	private byte[] BuildEmbedMessage(string embedGuid){
		List<byte> packetBytes = new List<byte>{
			5
		};

		packetBytes.AddRange(MakeDataSpan(GetClientId().ToUtf8Buffer())); // add user's guid
		packetBytes.AddRange(MakeDataSpan(embedGuid.ToUtf8Buffer()));

		return packetBytes.ToArray();
	}

	private byte[] BuildMsgPacket(string text){
		List<byte> packetList = new List<byte>
		{
			0
		};

		byte[] textBuffer = text.ToUtf8Buffer();
		byte[] encryptedMessage = null;

		byte[] key = GetSpaceKey(selectedSpaceId);
		byte[] keyId = ((string)GetSpace(selectedSpaceId)["keyId"]).ToUtf8Buffer();

		byte[] initVector = new byte[16];
		new Random().NextBytes(initVector);

		using (Aes aes = Aes.Create()){
			aes.Key = key;
			aes.IV = initVector;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;

			using (ICryptoTransform encryptor = aes.CreateEncryptor()){
				encryptedMessage = encryptor.TransformFinalBlock(textBuffer, 0, textBuffer.Length);
			}
		}

		packetList.AddRange(initVector);
		packetList.AddRange(MakeDataSpan(keyId));
		packetList.AddRange(MakeDataSpan(encryptedMessage));
		packetList.AddRange(MakeDataSpan(((string)clientUser["id"]).ToUtf8Buffer()));

		return packetList.ToArray();
	}

	private byte[] BuildIdentifyingPacket(){
		List<byte> packetBytes = new List<byte>{
			1
		};

		byte[] publicKey = clientAuth.ExportRSAPublicKey();
		byte[] username = ((string)clientUser["username"]).ToUtf8Buffer();
		byte[] guid = ((string)clientUser["id"]).ToUtf8Buffer();
		
		packetBytes.AddRange(MakeDataSpan(guid));
		packetBytes.AddRange(MakeDataSpan(username));
		packetBytes.AddRange(MakeDataSpan(publicKey));

		return packetBytes.ToArray();
	}

	private byte[] BuildSpaceInvite(byte[] recipientKey, string spaceGuid){
		List<byte> packetBytes = new List<byte>{
			4
		};
		GD.Print("Creating space invite for space: " + spaceGuid);

		Dictionary space = GetSpace(spaceGuid);

		string spaceName = (string)space["name"];

		byte[] spaceKey = GetSpaceKey(spaceGuid);;

		RSA inviteAuth = RSACryptoServiceProvider.Create(2048);
		inviteAuth.ImportRSAPublicKey(recipientKey, out int bytesRead);
		byte[] spaceKeyEncrypted = inviteAuth.Encrypt(spaceKey, RSAEncryptionPadding.Pkcs1);

		packetBytes.AddRange(MakeDataSpan(spaceGuid.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(spaceName.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(GetSpaceKeyId(spaceGuid).ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(spaceKeyEncrypted));

		return packetBytes.ToArray();
	}

	#endregion

	#region saveloaders

	private void SavePeers(){
		FileAccess peerFile = FileAccess.Open(clientPeerPath, FileAccess.ModeFlags.Write);
		peerFile.Seek(0);
		peerFile.StoreString(Json.Stringify(peers));
		peerFile.Close();
	}

	private void LoadPeers(){
		FileAccess userPeers = FileAccess.Open(clientPeerPath, FileAccess.ModeFlags.Read);
		string peerFileRaw = userPeers.GetAsText();

		peers = (Dictionary)Json.ParseString(peerFileRaw);
		userPeers.Close();
	}

	private void SaveSpaces(){
		FileAccess spaceFile = FileAccess.Open(clientSpacesPath, FileAccess.ModeFlags.Write);
		spaceFile.Seek(0);
		spaceFile.StoreString(Json.Stringify(spaces));
		spaceFile.Close();

		spacesList.Update(spaces);
	}

	private void LoadSpaces(){
		FileAccess userSpaces = FileAccess.Open(clientSpacesPath, FileAccess.ModeFlags.Read);
		string spaceFileRaw = userSpaces.GetAsText();

		spaces = (Dictionary)Json.ParseString(spaceFileRaw);
		userSpaces.Close();

		spacesList.Update(spaces);
	}

	public static void SaveUser(){
		FileAccess userFile = FileAccess.Open(clientSavePath, FileAccess.ModeFlags.Write);
		userFile.Seek(0);
		userFile.StoreLine(Json.Stringify(clientUser));
		userFile.Close();
	}

	public static void LoadUser(){
		FileAccess userData = FileAccess.Open(clientSavePath, FileAccess.ModeFlags.Read);
		string userfileRaw = userData.GetAsText();
		GD.Print("user: " + userfileRaw);
		Variant userParsed = Json.ParseString(userfileRaw);
		clientUser = (Dictionary)userParsed.Obj;
	}

	private void SaveKeys(){
		FileAccess keyFile = FileAccess.Open(knownKeysPath, FileAccess.ModeFlags.Write);

		Dictionary keysB64 = new Dictionary();
		foreach (KeyValuePair<Variant, Variant> entry in aesKeys){
			keysB64.Add((string)entry.Key, ToBase64((byte[])entry.Value));
		}

		keyFile.Seek(0);
		keyFile.StoreLine(Json.Stringify(keysB64));
		keyFile.Close();
	}

	private void LoadKeys(){
		FileAccess keyFile = FileAccess.Open(knownKeysPath, FileAccess.ModeFlags.Read);
		string keyFileRaw = keyFile.GetAsText();

		aesKeys = new Dictionary();

		foreach (KeyValuePair<Variant, Variant> entry in (Dictionary)Json.ParseString(keyFileRaw)){
			aesKeys.Add((string)entry.Key, FromBase64((string)entry.Value));
		}

		keyFile.Close();
	}

	private void SavePersonalKey(){
		FileAccess newKey = FileAccess.Open(clientKeyPath, FileAccess.ModeFlags.Write);
		
		byte[] privateKey = clientAuth.ExportRSAPrivateKey();

		newKey.StoreBuffer(privateKey);
		newKey.Close();
	}

	private void LoadPersonalKey(){
		FileAccess userKeyFile = FileAccess.Open(clientKeyPath, FileAccess.ModeFlags.Read);
		long keyLength = (long)userKeyFile.GetLength();

		clientAuth = new RSACryptoServiceProvider(2048);
		clientAuth.ImportRSAPrivateKey(userKeyFile.GetBuffer(keyLength), out int bytesRead);
		userKeyFile.Close();
	}

	#endregion

	#region library functions

	public static byte[] GetRandomBytes(int length){
		byte[] bytes = new byte[length];
		new Random().NextBytes(bytes);
		
		return bytes;
	}

	public static byte[] AESEncrypt(byte[] plaintext, byte[] key, byte[] iv){
		using (Aes aes = Aes.Create()){
			aes.Key = key;
			aes.IV = iv;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;

			using (ICryptoTransform encryptor = aes.CreateEncryptor()){
				return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
			}
		}
	}

	public static byte[] AESDecrypt(byte[] cyphertext, byte[] key, byte[] iv){
		using (Aes aes = Aes.Create()){
			aes.Key = key;
			aes.IV = iv;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;

			using (ICryptoTransform decryptor = aes.CreateDecryptor()){
				return decryptor.TransformFinalBlock(cyphertext, 0, cyphertext.Length);
			}
		}
	}

	public static string ToBase64(byte[] data){
		return Convert.ToBase64String(data);
	}

	public static byte[] FromBase64(string data){
		return Convert.FromBase64String(data);
	}

	public static byte[] ReadDataSpan(byte[] fullSpan, int startIndex){
		ushort spanLength = BitConverter.ToUInt16(fullSpan, startIndex);

		if (spanLength == 0){ // A dataspan length of zero assumes its a very large span at the end of the data
			return ReadLengthInfinetly(fullSpan, startIndex + 2);
		}

		return ReadLength(fullSpan, startIndex + 2, spanLength);
	}

	public static byte[][] ReadDataSpans(byte[] fullData, int startIndex){
		List<byte[]> spans = new List<byte[]>();
		for (int i = startIndex; i < fullData.Length; i += 0){ // increment not needed
			byte[] gotSpan = ReadDataSpan(fullData, i);
			spans.Add(gotSpan);
			i += gotSpan.Length + 2; // +2 accounts for length header
		}

		return spans.ToArray();
	}

	public static byte[] ReadLengthInfinetly(byte[] data, int startIndex){
		int length = data.Length - startIndex;

		byte[] read = new byte[length];
		
		for (int i = 0; i < length; i++){
			read[i] = data[i + startIndex];
		}

		return read;
	}

	public static byte[] ReadLength(byte[] data, int startIndex, int length){
		byte[] read = new byte[length];
		
		for (int i = 0; i < length; i++){
			read[i] = data[i + startIndex];
		}

		return read;
	}

	public static byte[] MakeDataSpan(byte[] data){
		if (data.Length > 32767)
			GD.PrintErr("Attempting to create a dataspan with a length of more than 32767. Consider overriding length to zero if this span is the last of a set.");
		return MakeDataSpan(data, (short)data.Length);
	}

	public static byte[] MakeDataSpan(byte[] data, short lengthHeaderOverride){
		List<byte> bytes = new List<byte>();
		short dataLength = lengthHeaderOverride;

		byte[] lengthHeader = BitConverter.GetBytes(dataLength);

		bytes.AddRange(lengthHeader);
		bytes.AddRange(data);

		return bytes.ToArray();
	}

	public static void PrintBytes(byte[] bytes){
		GD.Print("Bytes Print:");
		foreach (byte b in bytes){
			GD.Print("  " + b);
		}
	}

	public static void PrintBytes(byte[] bytes, string tag){
		GD.Print(tag);
		foreach (byte b in bytes){
			GD.Print("  " + b);
		}
	}

	public static byte[] SignData(byte[] data){
		byte[] signature = clientAuth.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		return signature;
	}

	public static bool VerifySigniture(byte[] data, byte[] signature, byte[] signeeKey){
		RSA signetureVerifier = RSA.Create();
		signetureVerifier.ImportRSAPublicKey(signeeKey, out int bytesRead);
		
		return signetureVerifier.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
	}

	public static float ByteToFloat(byte b){
		int bInt = b;
		return Mathf.Clamp(((float)(bInt + 1)/128) - 1, -1, 1);
	}

	public static byte FloatToByte(float f){
		float fUnsigned = f + 1;
		return (byte)(Mathf.FloorToInt(fUnsigned * 128) - 1);
	}

	public static float BytesToFloat(byte[] b, int index){
		// int bInt = b;
		return (float)BitConverter.ToSingle(b, index);
	}

	public static byte[] FloatToBytes(float f){
		float fUnsigned = f + 1;
		return BitConverter.GetBytes(f);
	}

	public static byte[] GetTwosComplement(byte[] number){
		byte[] newNum = number;
		for (int i = 0; i < number.Length; i++){
			newNum[i] = (byte)~number[i];
		}

		return BitConverter.GetBytes((ushort)(BitConverter.ToInt16(newNum) + 1));
	}

	public static byte[] GetChecksum(byte[] data){
		return GetTwosComplement(GetSumComplement(data));
	}

	public static byte[] GetSumComplement(byte[] data){
		ushort total = 0;
		for (int i = 0; i < Mathf.FloorToInt(data.Length / 2); i++){
			total = (ushort)(total + BitConverter.ToInt16(data, i * 2));
		}

		return BitConverter.GetBytes(total);
	}

	public static bool ValidateSumComplement(byte[] data, ushort checksum){
		ushort final = (ushort)(BitConverter.ToInt16(GetSumComplement(data)) + checksum);
		return final == 0;
	}

	public static byte[][] MakePartitions(byte[] data, int partitionSize){
		List<byte[]> partitions = new List<byte[]>();
		int dataIndex = 0;

		while(dataIndex < data.Length){
			byte[] partition = new byte[Mathf.Min(data.Length - dataIndex, partitionSize)];
			for (int i = 0; i < partition.Length; i++){
				partition[i] = data[dataIndex];

				dataIndex++;
			}
			partitions.Add(partition);
		}

		return partitions.ToArray();
	}

	#endregion

	public Dictionary GetPeerDict(){
		return peers;
	}

	// Debuggers
	public void DEBUGB64SpaceInvite(string invite){
		ProcessSpaceInvite(FromBase64(invite));
	}

	public string ToHexString(byte[] data){
		return BitConverter.ToString(data).Replace("-", "");
	}

	public struct User{
		public string username;
	}
}
