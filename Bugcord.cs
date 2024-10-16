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

	[Signal] public delegate void OnMessageRecievedEventHandler(Dictionary message);
	[Signal] public delegate void OnConnectedToSpaceEventHandler(string spaceId, string spaceName);

	public const int minAudioFrames = 2048;
	public const int maxAudioFrames = 4096;
	public const int audioFrames = 4096;

	public const int filePacketSize = 4096;

	public static string selectedSpaceId;
	public static string selectedKeyId; // Remove

	public enum MessageComponentFlags{
		None = 0,
		Text = 1,
		FileEmbed = 1 << 1,
		IsReply = 1 << 2,
	}

	private bool retrievingChain;

	private FileService fileService;
	private KeyService keyService;
	private UserService userService;
	private SpaceService spaceService;
	private PeerService peerService;
	private PopupAlert alertService;
	private DatabaseService databaseService;
	private PacketService packetService;
	private StreamService streamService;
	private EventChainService eventChainService;
	private RequestService requestService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		registerWindow.Visible = false;

		fileService = GetNode<FileService>("FileService");
		keyService = GetNode<KeyService>("KeyService");
		userService = GetNode<UserService>("UserService");
		spaceService = GetNode<SpaceService>("SpaceService");
		peerService = GetNode<PeerService>("PeerService");
		databaseService = GetNode<DatabaseService>("DatabaseService");
		packetService = GetNode<PacketService>("PacketService");
		streamService = GetNode<StreamService>("StreamService");
		eventChainService = GetNode<EventChainService>("EventChainService");
		requestService = GetNode<RequestService>("RequestService");
		alertService = GetNode<PopupAlert>("/root/Main/Popups/GenericPopup");

		if (!LogIn()){
			registerWindow.Visible = true;
		}
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest){
			packetService.Disconnect();
			GetTree().Quit();
		}
    }

	#region message functions

	public void DisplayMessage(DatabaseService.Message message){
		Dictionary messageDict = new Dictionary
		{
			{"sender", peerService.peers[message.senderId].username}
		};

		if (message.content != null){
			messageDict.Add("content", message.content);
		}

		if (message.embedId != null){
			messageDict.Add("mediaId", message.embedId);
		}

		EmitSignal(SignalName.OnMessageRecieved, (Dictionary)message);
	}

	public void PostMessage(string message, string[] embedPaths, string replyingTo){
		if (!CheckSendReady())
			return;

		if (message.Length == 0) // It appears that when null strings are sent through signals they are turned into zero length strings
			message = null;
		if (replyingTo.Length == 0)
			replyingTo = null;

		GD.Print(embedPaths.Length);

		if (embedPaths.Length == 0){ // If message contains no embeds
			Send(BuildMsgPacket(message, replyingTo, null));
			return;
		}

		for (int i = 0; i < embedPaths.Length; i++){ // Create a message for every embed
			string embedPath = embedPaths[i];
			string embedId = fileService.PrepareFile(embedPath, true, selectedKeyId);
			if (i == 0){ // However, only the first message should contain the text content and be a reply
				Send(BuildMsgPacket(message, replyingTo, embedId));
				continue;
			}

			Send(BuildMsgPacket(null, null, embedId));
		}
	}

	#endregion

	#region connection & websocket interaction

	public bool CheckSendReady(){
		if (keyService.userAuthentication == null){
			alertService.NewAlert("You must register an account");
			return false;
		}

		if (packetService.currentState != StreamPeerTcp.Status.Connected){
			alertService.NewAlert("You must connect to a Bugcord relay server", "Enter an ip address on the top left and press the # button to connect.");
			return false;
		}
		
		// packetService.UpdateConnectionState();
		// if (packetService.currentState != StreamPeerTcp.Status.Connected){
		// 	alertService.NewAlert("Not connected to relay", "Connection may have been lost. Press the # button on the top left to reconnect.");
		// 	return false;
		// }

		if (selectedSpaceId == null){
			alertService.NewAlert("Not connected to a space", "Either create your own space using the \"Create Space\" button on the left, or have a friend invite you to one and it will automatically appear in the list.");
			return false;
		}

		return true;
	}

	public void Connect(string url){
		if (keyService.userAuthentication == null){
			alertService.NewAlert("You must register an account");
			return;
		}

		userService.savedServerIp = url;
		userService.SaveToFile();

		PacketService.ParseUrl(url, out string host, out int port);
		packetService.Connect(host, port);

		streamService.Connect(host, port + 1);
	}

	public void Send(byte[] data){
		if (data.Length == 0)
			return;

		packetService.SendPacket(data);
	}

	public void SetAutoConnect(bool setTrue){
		userService.autoConnectToServer = setTrue;

		userService.SaveToFile();
	}

	public void OnConnected(){
		GD.Print("connected");

		Send(BuildIdentifyingPacket());
	}

	#endregion

	#region user file related

	public bool LogIn(){
		if (!FileAccess.FileExists(UserService.clientSavePath)){
			return false;
		}

		userService.LoadFromFile();

		// User RSA key
		if (!keyService.AuthLoadFromFile()){
			return false;
		}

		peerService.LoadFromFile();

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

	public string GetClientId(){
		return userService.userId;
	}

	#endregion

	#region space functions

	public void ConnectSpace(string guid){
		selectedSpaceId = guid;
		selectedKeyId = spaceService.spaces[guid].keyId;

		GD.Print("connected to space " + guid);
		AlertPanel.PostAlert("Connected to space", guid);

		EmitSignal(SignalName.OnConnectedToSpace, guid, spaceService.spaces[guid].name);
	}

	public void UpdateSpace(string spaceId){
		List<PeerService.Peer> members = spaceService.spaces[spaceId].members;
		foreach (PeerService.Peer member in members)
		{
			SendSpaceInvite(spaceId, member.id);
		}
	}

	public void SendSpaceInvite(string spaceGuid, string peerGuid){
		byte[] spaceInvitePacket = BuildKeyPackage(peerGuid, spaceService.spaces[spaceGuid].keyId);

		Send(spaceInvitePacket);

		Send(BuildSpaceUpdatePacket(spaceService.spaces[spaceGuid]));
	}

	#endregion

	#region packet processors

	public void ProcessIncomingPacket(PacketService.Packet packet){
		byte type = packet.data[0];
		GD.Print("Recieved packet. Type: " + type);

		switch (type){
			case 0:
				ProcessMessagePacket(packet);
				break;
			case 1:
				ProcessIdentify(packet.data);
				break;
			case 4:
				ProcessKeyPackage(packet.data);
				break;
			case 6:
				ProcessFileRequest(packet.data);
				break;
			case 7:
				requestService.ProcessRequestResponse(packet.data);
				// ProcessFilePacket(packet.data);
				break;
			case 9:
				ProcessSpaceUpdatePacket(packet);
				break;
		}
	}

	private void ProcessSpaceUpdatePacket(PacketService.Packet packet){
		byte[][] dataSpans = ReadDataSpans(packet.data, 9);

		int authorityCount = BitConverter.ToInt32(packet.data, 1);
		int memberCount = BitConverter.ToInt32(packet.data, 5);

		string spaceId = dataSpans[0].GetStringFromUtf8();
		string spaceName = dataSpans[1].GetStringFromUtf8();
		string keyId = dataSpans[2].GetStringFromUtf8();
		string ownerId = dataSpans[3].GetStringFromUtf8();
		PeerService.Peer owner = peerService.peers[ownerId];

		int memberOffset = 4;
		List<PeerService.Peer> authorities = new List<PeerService.Peer>();
		for (int i = 4; i < authorityCount + memberOffset; i++){
			string authorityId = dataSpans[i].GetStringFromUtf8();
			authorities.Add(peerService.peers[authorityId]);
			memberOffset++;
		}

		List<PeerService.Peer> members = new List<PeerService.Peer>();
		for (int i = memberOffset; i < memberCount + memberOffset; i++){
			string memberId = dataSpans[i].GetStringFromUtf8();
			authorities.Add(peerService.peers[memberId]);
		}

		spaceService.AddSpace(spaceId, spaceName, keyId, owner, authorities, members);
	}

	private void ProcessFilePacket(byte[] packet){
		GD.Print("recieved file packet");

		byte[][] dataSpans = ReadDataSpans(packet, 6);

		ushort filePart = BitConverter.ToUInt16(packet, 2);
		ushort filePartMax = BitConverter.ToUInt16(packet, 4);

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
		if (!fileService.HasServableFile(fileGuid)) // stop if we dont have this file
			return;

		byte[][] servePartitions = MakePartitions(fileService.GetServableData(fileGuid), filePacketSize);
		for(int i = 0; i < servePartitions.Length; i++){
			packetService.SendPacket(BuildFilePacket(fileGuid, i, servePartitions.Length, servePartitions[i], subtype));
		}

		// switch (subtype){
		// 	case 0:
		// 		string fileGuid = ReadDataSpan(packet, 2).GetStringFromUtf8();
		// 		if (!fileService.HasServableFile(fileGuid)) // stop if we dont have this file
		// 			return;

		// 		byte[][] servePartitions = MakePartitions(fileService.GetServableData(fileGuid), filePacketSize);
		// 		for(int i = 0; i < servePartitions.Length; i++){
		// 			packetService.SendPacket(BuildFilePacket(fileGuid, i, servePartitions.Length, servePartitions[i], subtype));
		// 		}
		// 		break;
		// 	case 1: // Eventfile request
		// 		long timestamp = BitConverter.ToInt64(packet, 3);
		// 		string requestedGuid = ReadDataSpan(packet, 10).GetStringFromUtf8();
		// 		List<PacketService.Packet> packets = databaseService.GetPackets(timestamp);
				
		// 		// Turn packets into single span of bytes
		// 		List<byte> mergedPackets = new List<byte>();
		// 		for (int i = 0; i < packets.Count; i++){
		// 			List<byte> packetBytes = new List<byte>();
		// 			packetBytes.AddRange(MakeDataSpan(BitConverter.GetBytes(packets[i].timestamp)));
		// 			packetBytes.AddRange(MakeDataSpan(packets[i].data));
		// 			mergedPackets.AddRange(MakeDataSpan(packetBytes.ToArray()));
		// 		}

		// 		byte[][] packetPartitions = MakePartitions(mergedPackets.ToArray(), filePacketSize);
		// 		for(int i = 0; i < packetPartitions.Length; i++){
		// 			packetService.SendPacket(BuildFilePacket(requestedGuid, i, packetPartitions.Length, packetPartitions[i]));
		// 		}
				
		// 		break;
		// }
	}

	private void ProcessKeyPackage(byte[] packet){
		GD.Print("Processing space invite");

		byte[][] dataSpans = ReadDataSpans(packet, 1);

		string keyId = dataSpans[0].GetStringFromUtf8();
		byte[] encryptedSpaceKey = dataSpans[1];

		byte[] spaceKey = new byte[32]; // Size of AES key in bytes. 256 bits = 32 bytes
		bool couldDecrypt = keyService.userAuthentication.TryDecrypt(encryptedSpaceKey, spaceKey, RSAEncryptionPadding.Pkcs1, out int bytesWritten);

		if (couldDecrypt){
			keyService.AddKey(keyId, spaceKey);
		}
	}

	private void ProcessIdentify(byte[] packet){
		byte[][] packetDataSpans = ReadDataSpans(packet, 1);

		string guid = packetDataSpans[0].GetStringFromUtf8();
		string username = packetDataSpans[1].GetStringFromUtf8();
		byte[] key = packetDataSpans[2];
		string profilePictureId = null;
		if (packetDataSpans[3].GetStringFromUtf8() != "null"){
			profilePictureId = packetDataSpans[3].GetStringFromUtf8();
		}

		if (peerService.AddPeer(guid, username, key, profilePictureId)){
			Send(BuildIdentifyingPacket());
		}
	}

	private void ProcessMessagePacket(PacketService.Packet packet){
		byte[][] spans = ReadDataSpans(packet.data, 19);

		ushort hashNonce =  BitConverter.ToUInt16(ReadLength(packet.data, 1, 2));
		byte[] initVector = ReadLength(packet.data, 3, 16);

		string keyUsed = spans[0].GetStringFromUtf8();
		if (!retrievingChain)
			eventChainService.SaveEvent(packet.data, keyUsed);

		byte[] encryptedSection = spans[1];

		byte[] decryptedSection = KeyService.AESDecrypt(encryptedSection, keyService.myKeys[keyUsed], initVector);

		byte[][] decryptedSpans = ReadDataSpans(decryptedSection, 0);
		string senderGuid = decryptedSpans[0].GetStringFromUtf8();
		MessageComponentFlags messageFlags = (MessageComponentFlags)BitConverter.ToUInt16(decryptedSpans[1]);

		// Read all components of the message based off of present flags
		int readingSpan = 2;
		string messageText = null;
		string embedId = null;
		string replyingTo = null;
		if (messageFlags.HasFlag(MessageComponentFlags.Text)){
			messageText = decryptedSpans[readingSpan].GetStringFromUtf8();
			readingSpan++;
		}

		if (messageFlags.HasFlag(MessageComponentFlags.FileEmbed)){
			embedId = decryptedSpans[readingSpan].GetStringFromUtf8();
			readingSpan++;
		}

		if (messageFlags.HasFlag(MessageComponentFlags.IsReply)){
			replyingTo = decryptedSpans[readingSpan].GetStringFromUtf8();
			readingSpan++;
		}

		DatabaseService.Message message = new DatabaseService.Message(){
			id = KeyService.GetSHA256HashString(packet.data),
			senderId = senderGuid,
			content = messageText,
			embedId = embedId,
			unixTimestamp = packet.timestamp,
			nonce = hashNonce,
			replyingTo = replyingTo,
		};

		DisplayMessage(message);
		databaseService.SaveMessage(spaceService.GetSpaceUsingKey(keyUsed), message);

		if (messageFlags.HasFlag(MessageComponentFlags.FileEmbed)){
			if (fileService.IsFileInCache(embedId)){
				fileService.EmitSignal(FileService.SignalName.OnCacheChanged, embedId); // Call the embed message ui to update and load the image from cache
				return;
			}

			requestService.Request(embedId, RequestService.FileExtension.MediaFile, RequestService.VerifyMethod.HashCheck);
			// Send(BuildFileRequest(embedId));
		}

		databaseService.SavePacket(packet);
	}

	#endregion

	#region packet builders

	private byte[] BuildSpaceUpdatePacket(SpaceService.Space space){
		List<byte> packetBytes = new List<byte>{
			9
		};

		packetBytes.AddRange(BitConverter.GetBytes(space.authorities.Count));
		packetBytes.AddRange(BitConverter.GetBytes(space.members.Count));

		packetBytes.AddRange(MakeDataSpan(space.id.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(space.name.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(space.keyId.ToUtf8Buffer()));

		packetBytes.AddRange(MakeDataSpan(space.owner.id.ToUtf8Buffer()));

		foreach (PeerService.Peer peer in space.authorities){
			packetBytes.AddRange(MakeDataSpan(peer.id.ToUtf8Buffer()));
		}

		foreach (PeerService.Peer peer in space.members){
			packetBytes.AddRange(MakeDataSpan(peer.id.ToUtf8Buffer()));
		}

		return packetBytes.ToArray();
	}

	private byte[] BuildFilePacket(string fileGuid, int filePart, int totalFileParts, byte[] data, byte subtype){
		List<byte> packetBytes = new List<byte>{
			7,
			subtype,
		};

		packetBytes.AddRange(BitConverter.GetBytes((ushort)filePart));
		packetBytes.AddRange(BitConverter.GetBytes((ushort)totalFileParts));
		packetBytes.AddRange(MakeDataSpan(fileGuid.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(GetClientId().ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(data, 0));

		return packetBytes.ToArray();
	}

	public byte[] BuildEventRequest(string eventFileGuid, long afterDate){
		List<byte> packetBytes = new List<byte>{
			6,
			1 // request subtype
		};
		
		packetBytes.AddRange(BitConverter.GetBytes(afterDate));
		packetBytes.AddRange(MakeDataSpan(eventFileGuid.ToUtf8Buffer()));

		return packetBytes.ToArray();
	}

	public byte[] BuildFileRequest(string fileGuid){
		return BuildFileRequest(fileGuid, 0);
	}

	public byte[] BuildFileRequest(string fileGuid, byte subtype){
		List<byte> packetBytes = new List<byte>{
			6,
			subtype,
		};

		packetBytes.AddRange(MakeDataSpan(fileGuid.ToUtf8Buffer()));

		return packetBytes.ToArray();
	}

	private byte[] BuildMsgPacket(string text, string replyingTo, string embedId){
		List<byte> packetList = new List<byte>
		{
			0
		};

		byte[] keyId = spaceService.spaces[selectedSpaceId].keyId.ToUtf8Buffer();

		byte[] hashNonce = new byte[2];
		byte[] initVector = new byte[16];
		new Random().NextBytes(initVector);
		new Random().NextBytes(hashNonce);

		MessageComponentFlags messageFlags = new MessageComponentFlags();
		if (text != null)
			messageFlags |= MessageComponentFlags.Text; // Set text flag
		if (replyingTo != null)
			messageFlags |= MessageComponentFlags.IsReply; // Set reply
		if (embedId != null)
			messageFlags |= MessageComponentFlags.FileEmbed; // Set embed

		List<byte> sectionToEncrypt = new List<byte>();
		sectionToEncrypt.AddRange(MakeDataSpan(userService.userId.ToUtf8Buffer())); // Sender id
		sectionToEncrypt.AddRange(MakeDataSpan(BitConverter.GetBytes((ushort)messageFlags))); // Message flags
		if (text != null)
			sectionToEncrypt.AddRange(MakeDataSpan(text.ToUtf8Buffer()));
		if (embedId != null)
			sectionToEncrypt.AddRange(MakeDataSpan(embedId.ToUtf8Buffer()));
		if (replyingTo != null)
			sectionToEncrypt.AddRange(MakeDataSpan(replyingTo.ToUtf8Buffer()));

		byte[] encryptedSection = keyService.EncryptWithSpace(sectionToEncrypt.ToArray(), selectedSpaceId, initVector);

		packetList.AddRange(hashNonce);
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
		byte[] profilePicture = "null".ToUtf8Buffer();
		if (userService.profilePictureFileId != null){
			profilePicture = userService.profilePictureFileId.ToUtf8Buffer();
		}
		
		packetBytes.AddRange(MakeDataSpan(guid));
		packetBytes.AddRange(MakeDataSpan(username));
		packetBytes.AddRange(MakeDataSpan(publicKey));
		packetBytes.AddRange(MakeDataSpan(profilePicture));

		return packetBytes.ToArray();
	}

	private byte[] BuildKeyPackage(string recipientId, string keyId){
		List<byte> packetBytes = new List<byte>{
			4
		};

		byte[] spaceKeyEncrypted = keyService.EncryptKeyForPeer(keyId, recipientId);

		packetBytes.AddRange(MakeDataSpan(keyId.ToUtf8Buffer()));
		packetBytes.AddRange(MakeDataSpan(spaceKeyEncrypted));

		return packetBytes.ToArray();
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

	// Debuggers
	public void DEBUGB64SpaceInvite(string invite){
		ProcessKeyPackage(FromBase64(invite));
	}

	public string ToHexString(byte[] data){
		return BitConverter.ToString(data).Replace("-", "");
	}

	public struct User{
		public string username;
	}
}
