using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

public partial class Bugcord : Node
{
	[Export] public TextEdit messageInput;
	[Export] public Control registerWindow;

	[Export] public ServerSelector serverSelector;

	[Export] public AudioStreamPlayer audioRecorder;
	[Export] public AudioStreamPlayer audioPlayer;

	[Export] public CheckBox voiceChatToggle;

	[Signal] public delegate void OnMessageRecievedEventHandler(Dictionary message);
	[Signal] public delegate void OnEmbedMessageRecievedEventHandler(Dictionary message);
	[Signal] public delegate void OnConnectedToSpaceEventHandler(string spaceId, string spaceName);

	public const string clientPeerPath = "user://peers.json";

	public const string knownKeysPath = "user://keys.auth";

	public const int minAudioFrames = 2048;
	public const int maxAudioFrames = 4096;
	public const int audioFrames = 4096;

	public const int defaultPort = 25987;

	public const int filePacketSize = 4096;

	public static Dictionary peers;

	public static List<byte> incomingPacketBuffer = new List<byte>();
	public static List<byte[]> outgoingPacketBuffer = new List<byte[]>();

	public static System.Collections.Generic.Dictionary<string, List<byte>> incomingVoiceBuffer = new();

	public static string selectedSpaceId;
	public static string selectedKeyId;

	public enum MessageComponentFlags{
		None = 0,
		Text = 1,
		FileEmbed = 1 << 1,
	}

	private static StreamPeerTcp tcpClient;
	private static PacketPeerUdp udpClient;

	private static StreamPeerTcp.Status previousState;

	private static Bugcord instance;

	private static AudioEffectCapture recordBusCapture;
	private static AudioStreamGeneratorPlayback voicePlaybackBus;

	private static Vector2[] currentFrameAudio;

	private FileService fileService;
	private KeyService keyService;
	private UserService userService;
	private SpaceService spaceService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		instance = this;

		registerWindow.Visible = false;

		fileService = GetNode<FileService>("FileService");
		keyService = GetNode<KeyService>("KeyService");
		userService = GetNode<UserService>("UserService");
		spaceService = GetNode<SpaceService>("SpaceService");

		if (!LogIn()){
			registerWindow.Visible = true;
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (tcpClient == null || keyService.userAuthentication == null){
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

		if (tcpClient.GetAvailableBytes() > 0){
			Godot.Collections.Array recieved = tcpClient.GetData(tcpClient.GetAvailableBytes());
			byte[] rawPacket = (byte[])recieved[1];
			incomingPacketBuffer.AddRange(rawPacket);

			for (int i = 0; i < 10; i++){ // Process multiple packets at once
				bool processResult = ProcessRawPacket(incomingPacketBuffer.ToArray(), out int usedPacketLength);
				incomingPacketBuffer.RemoveRange(0, usedPacketLength);

				if (!processResult)
					break;
			}
		}

		// Send a pending packet
		if (outgoingPacketBuffer.Count > 0){
			Send(outgoingPacketBuffer[0]);
			outgoingPacketBuffer.RemoveAt(0);
		}

		if (!udpClient.IsSocketConnected()){
			return;
		}

		if (voiceChatToggle.ButtonPressed){
			Vector2[] vBuffer = GetVoiceBuffer(audioFrames);
			if (vBuffer != null)
				Send(BuildVoicePacket(vBuffer), true);
		}

		while (udpClient.GetAvailablePacketCount() > 0){
			byte[] p = udpClient.GetPacket();
			ProcessIncomingPacket(p);
		}

		int vFrames = 0;
		currentFrameAudio = new Vector2[audioFrames];
		foreach (KeyValuePair<string, List<byte>> entry in incomingVoiceBuffer){
			if (entry.Key == userService.userId)
				continue;
			if (entry.Value.Count < audioFrames)
				continue;

			for (int i = 0; i < currentFrameAudio.Length; i++){
				float f = ByteToFloat(entry.Value[i]);
				vFrames++;
				currentFrameAudio[i] += new Vector2(f, f);
			}
			entry.Value.RemoveRange(0, audioFrames);
		}
		
		if (vFrames > 0)
			voicePlaybackBus.PushBuffer(currentFrameAudio);
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

	public Vector2[] GetVoiceBuffer(int bufferSize){
		int framesAvailable = recordBusCapture.GetFramesAvailable();
		
		if (framesAvailable >= bufferSize){
			Vector2[] frames = recordBusCapture.GetBuffer(bufferSize);
			return frames;
		}
		
		return null;
	}

	#endregion

    #region server client

    // prepares a file to be served and sends an embed linked message
    public void SubmitEmbed(string directory){
		string guidString = Guid.NewGuid().ToString();
		string filename = System.IO.Path.GetFileName(directory);

		GD.Print("preparing embedded file " + filename);
		
		FileAccess embedFile = FileAccess.Open(directory, FileAccess.ModeFlags.Read);
		byte[] embedData = embedFile.GetBuffer((long)embedFile.GetLength());
		byte[] servableData = fileService.TransformRealFile(embedData, filename, guidString, true);
		fileService.WriteToCache(embedData, filename, guidString);
		fileService.WriteToServable(servableData, guidString);

		Send(BuildEmbedMessage(guidString));
	}

	#endregion

	#region message functions

	public void DisplayMessage(string senderId, string content, string mediaId){
		Dictionary messageDict = new Dictionary
		{
			{"sender", ((Dictionary)peers[senderId])["username"]}
		};

		if (content != null){
			messageDict.Add("content", content);
		}

		if (mediaId != null){
			messageDict.Add("mediaId", mediaId);
		}

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
		if (keyService.userAuthentication == null){
			GD.Print("no auth");
			return;
		}

		userService.savedServerIp = url;
		userService.SaveToFile();

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
		udpClient.ConnectToHost(urlHost, urlPort + 1);
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
		userService.autoConnectToServer = setTrue;

		userService.SaveToFile();
	}

	public void OnConnected(){
		GD.Print("connected");
		Send(BuildIdentifyingPacket());
		InitVoice();
	}

	#endregion

	#region user file related

	public bool LogIn(){
		if (!FileAccess.FileExists(UserService.clientSavePath)){
			return false;
		}
		if (!FileAccess.FileExists(KeyService.clientKeyPath)){
			return false;
		}

		if (!FileAccess.FileExists(clientPeerPath)){
			MakeNewPeerFile();
		}

		userService.LoadFromFile();

		// User RSA key
		keyService.AuthLoadFromFile();

		LoadPeers();

	 	spaceService.LoadFromFile();

		keyService.KeysLoadFromFile();

		userService.EmitSignal(UserService.SignalName.OnLoggedIn);

		if (userService.autoConnectToServer){
			Connect(userService.savedServerIp);
		}

		return true;
	}

	public void CreateUserFile(string username, string password){
		GD.Print("Creating new user.. " + username);

		userService.MakeNewUser(username, password);
		userService.SaveToFile();

		keyService.NewUserAuth();
		keyService.AuthSaveToFile();

		LogIn();
	}

	private void MakeNewPeerFile(){
		FileAccess peerList = FileAccess.Open(clientPeerPath, FileAccess.ModeFlags.Write);
		Godot.Collections.Dictionary<string, string> peerDict = new();
		peerList.StoreString(Json.Stringify(peerDict));
		peerList.Close();
	}

	public string GetClientId(){
		return userService.userId;
	}

	#endregion

	#region space functions

	public void ConnectSpace(string guid){
		selectedSpaceId = guid;
		selectedKeyId = spaceService.spaces[guid]["keyId"];

		GD.Print("connected to space " + guid);
		AlertPanel.PostAlert("Connected to space", guid);

		EmitSignal(SignalName.OnConnectedToSpace, guid, spaceService.spaces[guid]["name"]);
	}

	public void SendSpaceInvite(string spaceGuid, string peerGuid){
		byte[] spaceInvitePacket = BuildSpaceInvite(peerGuid, spaceGuid);

		Send(spaceInvitePacket);
	}

	#endregion

	#region packet processors

	private bool ProcessRawPacket(byte[] data, out int usedPacketLength){
		usedPacketLength = 0;

		if (data.Length < 6){
			return false;
		}

		int version = BitConverter.ToInt16(data, 0);
		short checksum = BitConverter.ToInt16(data, 2);
		ushort length = BitConverter.ToUInt16(data, 4);

		if (length > data.Length - 6){
			return false;
		}

		byte[] packet = ReadLength(data, 6, length);

		if (!ValidateSumComplement(packet, (ushort)checksum)){
			return false;
		}

		if (packet.Length == 0){
			return false;
		}

		GD.Print("Checksum verified.");
		ProcessIncomingPacket(packet);
		usedPacketLength = length + 6;
		return true;
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

		string senderId = dataSpans[0].GetStringFromUtf8();
		byte[] framesEncoded = dataSpans[1];

		incomingVoiceBuffer.TryAdd(senderId, new List<byte>());
		incomingVoiceBuffer[senderId].AddRange(framesEncoded);
	}

	private void ProcessFilePacket(byte[] packet){
		GD.Print("recieved file packet");

		byte[][] dataSpans = ReadDataSpans(packet, 5);

		ushort filePart = BitConverter.ToUInt16(packet, 1);
		ushort filePartMax = BitConverter.ToUInt16(packet, 3);

		string fileGuid = dataSpans[0].GetStringFromUtf8();
		string senderGuid = dataSpans[1].GetStringFromUtf8();
		byte[] fileData = dataSpans[2];

		if (fileService.IsFileInCache(fileGuid)){ // File already in cache
			return;
		}

		fileService.UpdateFileBuffer(filePart, filePartMax, fileGuid, senderGuid, fileData);
	}

	private void ProcessFileRequest(byte[] packet){
		GD.Print("Recieved file request");
		
		byte subtype = packet[1];
		string fileGuid = ReadDataSpan(packet, 2).GetStringFromUtf8();

		switch (subtype){
			case 0:
				if (!fileService.HasServableFile(fileGuid)) // stop if we dont have this file
					return;

				byte[][] servePartitions = MakePartitions(fileService.GetServableData(fileGuid), filePacketSize);
				for(int i = 0; i < servePartitions.Length; i++){
					outgoingPacketBuffer.Add(BuildFilePacket(fileGuid, i, servePartitions.Length, servePartitions[i]));
				}
				break;
		}
	}

	private void ProcessSpaceInvite(byte[] packet){
		GD.Print("Processing space invite");

		byte[][] dataSpans = ReadDataSpans(packet, 1);

		string uuid = dataSpans[0].GetStringFromUtf8();
		string spaceName = dataSpans[1].GetStringFromUtf8();
		string keyId = dataSpans[2].GetStringFromUtf8();
		byte[] encryptedSpaceKey = dataSpans[3];

		byte[] spaceKey = new byte[32]; // Size of AES key in bytes. 256 bits = 32 bytes
		bool couldDecrypt = keyService.userAuthentication.TryDecrypt(encryptedSpaceKey, spaceKey, RSAEncryptionPadding.Pkcs1, out int bytesWritten);

		if (couldDecrypt){
			spaceService.AddSpace(uuid, spaceName, keyId);
			keyService.AddKey(keyId, spaceKey);
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
			keyService.peerKeys.Add(guid, packetDataSpans[2]);
			SavePeers();
			Send(BuildIdentifyingPacket());
		}
	}

	private void ProcessMessagePacket(byte[] packet){
		byte[][] spans = ReadDataSpans(packet, 17);

		byte[] initVector = ReadLength(packet, 1, 16);

		string keyUsed = spans[0].GetStringFromUtf8();
		byte[] encryptedSection = spans[1];

		byte[] decryptedSection = KeyService.AESDecrypt(encryptedSection, keyService.myKeys[keyUsed], initVector);

		byte[][] decryptedSpans = ReadDataSpans(decryptedSection, 0);
		string senderGuid = decryptedSpans[0].GetStringFromUtf8();
		MessageComponentFlags messageFlags = (MessageComponentFlags)BitConverter.ToUInt16(decryptedSpans[1]);

		// Read all components of the message based off of present flags
		int readingSpan = 2;
		string messageText = null;
		string embedId = null;
		if (messageFlags.HasFlag(MessageComponentFlags.Text)){
			messageText = decryptedSpans[readingSpan].GetStringFromUtf8();
			readingSpan++;
		}

		if (messageFlags.HasFlag(MessageComponentFlags.FileEmbed)){
			embedId = decryptedSpans[readingSpan].GetStringFromUtf8();
			readingSpan++;
		}

		DisplayMessage(senderGuid, messageText, embedId);

		if (messageFlags.HasFlag(MessageComponentFlags.FileEmbed)){
			if (fileService.IsFileInCache(embedId)){
				fileService.EmitSignal(FileService.SignalName.OnCacheChanged, embedId); // Call the embed message ui to update and load the image from cache
				return;
			}

			Send(BuildFileRequest(embedId));
		}
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

		packetBytes.AddRange(MakeDataSpan(userService.userId.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(codedFrames, 0));

		return packetBytes.ToArray();
	}

	private byte[] BuildFilePacket(string fileGuid, int filePart, int totalFileParts, byte[] data){
		List<byte> packetBytes = new List<byte>{
			7
		};

		packetBytes.AddRange(BitConverter.GetBytes((ushort)filePart));
		packetBytes.AddRange(BitConverter.GetBytes((ushort)totalFileParts));
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
		// List<byte> packetBytes = new List<byte>{
		// 	5
		// };

		// packetBytes.AddRange(MakeDataSpan(GetClientId().ToUtf8Buffer())); // add user's guid
		// packetBytes.AddRange(MakeDataSpan(embedGuid.ToUtf8Buffer()));

		// return packetBytes.ToArray();

		List<byte> packetList = new List<byte>
		{
			0
		};

		byte[] keyId = spaceService.spaces[selectedSpaceId]["keyId"].ToUtf8Buffer();

		byte[] initVector = new byte[16];
		new Random().NextBytes(initVector);

		MessageComponentFlags messageFlags = new MessageComponentFlags();
		messageFlags |= MessageComponentFlags.FileEmbed; // Set embed

		List<byte> sectionToEncrypt = new List<byte>();
		sectionToEncrypt.AddRange(MakeDataSpan(userService.userId.ToUtf8Buffer())); // Sender id
		sectionToEncrypt.AddRange(MakeDataSpan(BitConverter.GetBytes((ushort)messageFlags))); // Message flags
		sectionToEncrypt.AddRange(MakeDataSpan(embedGuid.ToUtf8Buffer()));
		
		byte[] encryptedSection = keyService.EncryptWithSpace(sectionToEncrypt.ToArray(), selectedSpaceId, initVector);

		packetList.AddRange(initVector);
		packetList.AddRange(MakeDataSpan(keyId));
		packetList.AddRange(MakeDataSpan(encryptedSection));

		return packetList.ToArray();
	}

	private byte[] BuildMsgPacket(string text){
		List<byte> packetList = new List<byte>
		{
			0
		};

		byte[] keyId = spaceService.spaces[selectedSpaceId]["keyId"].ToUtf8Buffer();

		byte[] initVector = new byte[16];
		new Random().NextBytes(initVector);

		MessageComponentFlags messageFlags = new MessageComponentFlags();
		messageFlags |= MessageComponentFlags.Text; // Set text flag

		List<byte> sectionToEncrypt = new List<byte>();
		sectionToEncrypt.AddRange(MakeDataSpan(userService.userId.ToUtf8Buffer())); // Sender id
		sectionToEncrypt.AddRange(MakeDataSpan(BitConverter.GetBytes((ushort)messageFlags))); // Message flags
		sectionToEncrypt.AddRange(MakeDataSpan(text.ToUtf8Buffer()));

		byte[] encryptedSection = keyService.EncryptWithSpace(sectionToEncrypt.ToArray(), selectedSpaceId, initVector);

		packetList.AddRange(initVector);
		packetList.AddRange(MakeDataSpan(keyId));
		packetList.AddRange(MakeDataSpan(encryptedSection));

		return packetList.ToArray();
	}

	private byte[] BuildIdentifyingPacket(){
		List<byte> packetBytes = new List<byte>{
			1
		};

		byte[] publicKey = keyService.userAuthentication.ExportRSAPublicKey();
		byte[] username = userService.userName.ToUtf8Buffer();
		byte[] guid = userService.userId.ToUtf8Buffer();
		
		packetBytes.AddRange(MakeDataSpan(guid));
		packetBytes.AddRange(MakeDataSpan(username));
		packetBytes.AddRange(MakeDataSpan(publicKey));

		return packetBytes.ToArray();
	}

	private byte[] BuildSpaceInvite(string recipientId, string spaceGuid){
		List<byte> packetBytes = new List<byte>{
			4
		};
		GD.Print("Creating space invite for space: " + spaceGuid);

		string spaceName = spaceService.spaces[spaceGuid]["name"];

		byte[] spaceKeyEncrypted = keyService.EncryptKeyForPeer(spaceService.spaces[spaceGuid]["keyId"], recipientId);

		packetBytes.AddRange(MakeDataSpan(spaceGuid.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(spaceName.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(spaceService.spaces[spaceGuid]["keyId"].ToUtf8Buffer()));
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
		foreach (KeyValuePair<Variant, Variant> peer in peers){
			keyService.peerKeys.Add((string)peer.Key, FromBase64((string)((Dictionary)peer.Value)["rsapublickey"]));
		}
		userPeers.Close();
	}

	#endregion

	#region library functions

	public static byte[] GetRandomBytes(int length){
		byte[] bytes = new byte[length];
		new Random().NextBytes(bytes);
		
		return bytes;
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
