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

	[Signal] public delegate void OnMessageRecievedEventHandler(Dictionary message);
	[Signal] public delegate void OnLoggedInEventHandler(Dictionary client);

	public const string clientSavePath = "user://client.data";
	public const string clientKeyPath = "user://client.auth";
	public const string clientPeerPath = "user://peers.json";
	public const string clientSpacesPath = "user://spaces.json";

	public static Dictionary clientUser;

	public static Dictionary peers;
	public static Dictionary spaces;

	public static byte[] selectedSpaceKey;

	private static RSA clientAuth;
	private static WebSocketPeer client;

	private static WebSocketPeer.State previousState;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		registerWindow.Visible = false;

		if (!LogIn()){
			registerWindow.Visible = true;
        }
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (client == null || clientAuth == null){
			return;
		}
		
		client.Poll();
		
		if (client.GetReadyState() == WebSocketPeer.State.Open && client.GetReadyState() != previousState){
			OnConnected();
		}

		previousState = client.GetReadyState();
		
		while (client.GetAvailablePacketCount() > 0){
			ProcessIncomingPacket(client.GetPacket());
		}
	}

	public void DisplayMessage(string content, string senderId){
		Dictionary messageDict = new Dictionary
		{
			{"content", content},
			{"sender", ((Dictionary)peers[senderId])["username"]}
		};

		EmitSignal(SignalName.OnMessageRecieved, messageDict);
	}

	public void Connect(string url){
		if (clientAuth == null){
			GD.Print("no auth");
			return;
		}

		clientUser["defaultConnectServer"] = url;
		
		GD.Print("connecting..");
		client = new WebSocketPeer();
		client.ConnectToUrl(url);
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
		client.Send(BuildIdentifyingPacket());
	}

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

		LoadUser();

		// User RSA key
		LoadKey();

		LoadPeers();

		LoadSpaces();

		EmitSignal(SignalName.OnLoggedIn, clientUser);

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
			{"defaultConnectServer", "ws://75.71.255.149:25987"},
			{"autoConnectToServer", "false"}
		};
		SaveUser();

		clientAuth = new RSACryptoServiceProvider(2048);
		SaveKey();

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

	public void GenerateSpace(string name){
		Aes spaceKey = Aes.Create();

		Dictionary spaceData = new Dictionary(){
			{"name", name},
			{"key", ToBase64(spaceKey.Key)}
		};

		spaces.Add(Guid.NewGuid().ToString(), spaceData);
		SaveSpaces();
	}

	public void ConnectSpace(string guid){
		selectedSpaceKey = FromBase64((string)((Dictionary)spaces[guid])["key"]);
		GD.Print("connected to space " + guid);
		AlertPanel.PostAlert("Connected to space", guid);
	}

	public void SendSpaceInvite(string spaceGuid, string peerGuid){
		string recipiantKeyB64 = (string)((Dictionary)peers[peerGuid])["rsapublickey"];
		byte[] recipiantKey = FromBase64(recipiantKeyB64);;

		byte[] spaceInvitePacket = BuildSpaceInvite(recipiantKey, spaceGuid);

		client.Send(spaceInvitePacket);
	}

	public void PostMessage(string message){
		// client.SendText(message);
		client.Send(BuildMsgPacket(message));
	}

	public Dictionary GetPeerDict(){
		return peers;
	}

	private void ProcessIncomingPacket(byte[] packet){
		byte type = packet[0];

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
		}
	}

	private void ProcessSpaceInvite(byte[] packet){
		GD.Print("Processing space invite");

		byte[][] dataSpans = ReadDataSpans(packet, 1);

		byte[] uuid = dataSpans[0];
		byte[] spaceName = dataSpans[1];
		byte[] encryptedSpaceKey = dataSpans[2];

		if (spaces.ContainsKey(uuid.GetStringFromUtf8())){
			GD.Print("client already in space");
			return;
		}

		byte[] spaceKey = new byte[32]; // Size of AES key in bytes. 256 bits = 32 bytes
		bool couldDecrypt = clientAuth.TryDecrypt(encryptedSpaceKey, spaceKey, RSAEncryptionPadding.Pkcs1, out int bytesWritten);

		if (couldDecrypt){
			Dictionary spaceData = new Dictionary(){
				{"name", spaceName.GetStringFromUtf8()},
				{"key", ToBase64(spaceKey)}
			};
			
			spaces.Add(uuid.GetStringFromUtf8(), spaceData);
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
			client.Send(BuildIdentifyingPacket());
		}
	}

	private void ProcessMessagePacket(byte[] packet){
		byte[][] spans = ReadDataSpans(packet, 17);

		byte[] initVector = ReadLength(packet, 1, 16);

		byte[] encryptedMessage = spans[0];
		byte[] senderGuid = spans[1];

		byte[] decryptedMessage = null;

		using (Aes aes = Aes.Create()){
			aes.Key = selectedSpaceKey;
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

	// Format
	// First byte is message content index
	// Other bytes are: Data length, and data

	// Index byte format: (big endian)
	// First bit: contains text

	private byte[] BuildMsgPacket(string text){
		List<byte> packetList = new List<byte>
        {
            0
        };

		byte[] textBuffer = text.ToUtf8Buffer();
		byte[] encryptedMessage = null;

		byte[] key = selectedSpaceKey;

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

		string spaceName = (string)((Dictionary)spaces[spaceGuid])["name"];

		string spaceKeyB64 = (string)((Dictionary)spaces[spaceGuid])["key"];
		byte[] spaceKey = FromBase64(spaceKeyB64);

		RSA inviteAuth = RSACryptoServiceProvider.Create(2048);
		inviteAuth.ImportRSAPublicKey(recipientKey, out int bytesRead);
		byte[] spaceKeyEncrypted = inviteAuth.Encrypt(spaceKey, RSAEncryptionPadding.Pkcs1);

		packetBytes.AddRange(MakeDataSpan(spaceGuid.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(spaceName.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(spaceKeyEncrypted));

		return packetBytes.ToArray();
	}

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

	private void SaveUser(){
		FileAccess userFile = FileAccess.Open(clientSavePath, FileAccess.ModeFlags.Write);
		userFile.Seek(0);
		userFile.StoreLine(Json.Stringify(clientUser));
		userFile.Close();
	}

	private void LoadUser(){
		FileAccess userData = FileAccess.Open(clientSavePath, FileAccess.ModeFlags.Read);
		string userfileRaw = userData.GetAsText();
		GD.Print("user: " + userfileRaw);
		Variant userParsed = Json.ParseString(userfileRaw);
		clientUser = (Dictionary)userParsed.Obj;
	}

	private void SaveKey(){
		FileAccess newKey = FileAccess.Open(clientKeyPath, FileAccess.ModeFlags.Write);
		
		byte[] privateKey = clientAuth.ExportRSAPrivateKey();

		newKey.StoreBuffer(privateKey);
		newKey.Close();
	}

	private void LoadKey(){
		FileAccess userKeyFile = FileAccess.Open(clientKeyPath, FileAccess.ModeFlags.Read);
		long keyLength = (long)userKeyFile.GetLength();

		clientAuth = new RSACryptoServiceProvider(2048);
		clientAuth.ImportRSAPrivateKey(userKeyFile.GetBuffer(keyLength), out int bytesRead);
		userKeyFile.Close();
	}

	public static string ToBase64(byte[] data){
		return Convert.ToBase64String(data);
	}

	public static byte[] FromBase64(string data){
		return Convert.FromBase64String(data);
	}

	public static byte[] ReadDataSpan(byte[] fullSpan, int startIndex){
		ushort spanLength = BitConverter.ToUInt16(fullSpan, startIndex);
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

	public static byte[] ReadLength(byte[] data, int startIndex, int length){
		byte[] read = new byte[length];
		
		for (int i = 0; i < length; i++){
			read[i] = data[i + startIndex];
		}

		return read;
	}

	public static byte[] MakeDataSpan(byte[] data){
		List<byte> bytes = new List<byte>();
		byte[] lengthHeader = BitConverter.GetBytes((short)data.Length);

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

	// Debuggers
	public void DEBUGB64SpaceInvite(string invite){
		ProcessSpaceInvite(FromBase64(invite));
	}

	public struct User{
		public string username;
	}
}
