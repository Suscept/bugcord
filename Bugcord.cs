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
	private NotificationService notificationService;

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
		notificationService = GetNode<NotificationService>("NotificationService");
		alertService = GetNode<PopupAlert>("/root/Main/Popups/GenericPopup");

		if (!TryLogIn()){
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
			fileService.ClearCache();
			GetTree().Quit();
		}
    }

	#region message functions

	public void DisplayMessage(DatabaseService.Message message){
		Dictionary messageDict = new Dictionary
		{
			{"sender", peerService.GetPeer(message.senderId).username}
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
		if (!keyService.HasUserAuth()){
			alertService.NewAlert("You must register an account");
			return false;
		}

		if (packetService.currentState != StreamPeerTcp.Status.Connected){
			alertService.NewAlert("You must connect to a Bugcord relay server", "Enter an ip address on the top left and press the # button to connect.");
			return false;
		}
		
		if (selectedSpaceId == null){
			alertService.NewAlert("Not connected to a space", "Either create your own space using the \"Create Space\" button on the left, or have a friend invite you to one and it will automatically appear in the list.");
			return false;
		}

		return true;
	}

	public void Connect(string url){
		if (!keyService.HasUserAuth()){
			alertService.NewAlert("You must register an account");
			return;
		}

		userService.savedServerIp = url;
		userService.SaveClientConfig();

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

		userService.SaveClientConfig();
	}

	public void OnConnected(){
		GD.Print("connected");

		Send(BuildFileAvailabilityPacket(peerService.GetLocalPeer().id, RequestService.FileExtension.PeerData, RequestService.VerifyMethod.NewestSignature));
	}

	#endregion

	#region user file related

	public bool LogIn(){
		GD.Print("Logging in..");

		// User RSA key
		if (!keyService.AuthLoadFromFile()){
			return false;
		}

		userService.localPeer = peerService.GetPeer(keyService.GetUserIdFromAuth());
		userService.LoadClientConfig();

	 	spaceService.LoadFromFile();

		keyService.KeysLoadFromFile();

		userService.EmitSignal(UserService.SignalName.OnLoggedIn);

		if (userService.autoConnectToServer){
			Connect(userService.savedServerIp);
		}

		return true;
	}

	/// <summary>
	/// Attempts to log in to whatever user account the current key file represents.
	/// </summary>
	/// <returns>If login was successful</returns>
	public bool TryLogIn(){
		// Attempt to load the key file
		if (!keyService.AuthLoadFromFile()){
			return false; // It's currently not possible to log in without the private key file being on disk
		}

		return TryLogIn(keyService.GetUserIdFromAuth(), null);
	}

	public bool TryLogIn(string userId, string password){
		GD.Print("Trying login ID: " + userId);

		// Attempt to load the key file
		if (!keyService.AuthLoadFromFile()){
			return false; // It's currently not possible to log in without the private key file being on disk
		}

		if (!fileService.IsFileServable(userId, RequestService.FileExtension.PeerData)){
			GD.Print("- No peer data file");
			userService.MakePeerFile();
			alertService.NewAlert(LangFiles.Get("no_peerfile_at_login_title"), LangFiles.Get("no_peerfile_at_login_subtext"));
		}

		return LogIn();
	}

	public void CreateUserFile(string username, string password){
		GD.Print("Creating new user.. " + username);

		keyService.NewUserAuth();
		keyService.AuthSaveToFile();

		userService.MakePeerFile();
		userService.localPeer.username = username;
		// userService.MakeNewUser(username, password);
		userService.SaveClientConfig();

		LogIn();
		alertService.NewAlert(LangFiles.Get("register_welcome_title"), LangFiles.Get("register_welcome_subtext"), LangFiles.Get("register_welcome_button"));
	}

	public string GetClientId(){
		return userService.localPeer.id;
	}

	#endregion

	# region chain loading

	public void FinishRetrivingChain(){
		GD.Print("Finished retriving chain");
		retrievingChain = false;
	}
	
	#endregion

	#region space functions

	public void ConnectSpace(string guid){
		selectedSpaceId = guid;
		selectedKeyId = spaceService.spaces[guid].keyId;

		GD.Print("connected to space " + guid);
		AlertPanel.PostAlert("Connected to space", guid);

		EmitSignal(SignalName.OnConnectedToSpace, guid, spaceService.spaces[guid].name);

		// retrievingChain = true;
		eventChainService.LoadEventChain(guid);
	}

	public void UpdateSpace(string spaceId){
		HashSet<PeerService.Peer> members = spaceService.spaces[spaceId].members;
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

	public void ProcessIncomingPacket(PacketService.Packet packet, bool fromEventChain){
		byte typeNum = packet.data[0];
		PacketService.PacketType type = (PacketService.PacketType)typeNum;

		if (fromEventChain){
			GD.Print("Loaded packet from event chain. Type: " + typeNum + ", " + type.ToString());
		}else{
			GD.Print("Recieved packet. Type: " + typeNum + ", " + type.ToString());
		}

		switch (type){
			case PacketService.PacketType.Message:
				ProcessMessagePacket(packet, fromEventChain);
				break;
			case PacketService.PacketType.Identify:
				ProcessIdentify(packet.data);
				break;
			case PacketService.PacketType.KeyPackage:
				ProcessKeyPackage(packet.data);
				break;
			case PacketService.PacketType.FileRequest:
				ProcessFileRequest(packet.data);
				break;
			case PacketService.PacketType.FilePacket:
				requestService.ProcessRequestResponse(packet.data);
				// ProcessFilePacket(packet.data);
				break;
			case PacketService.PacketType.SpaceUpdate:
				ProcessSpaceUpdatePacket(packet, fromEventChain);
				break;
			case PacketService.PacketType.FileAvailable:
				ProcessFileAvailabilityPacket(packet);
				break;
		}
	}

	private void ProcessFileAvailabilityPacket(PacketService.Packet packet){
		RequestService.FileExtension extension = (RequestService.FileExtension)packet.data[1];
		RequestService.VerifyMethod verifyMethod = (RequestService.VerifyMethod)packet.data[2];

		string fileId = Buglib.ReadDataSpan(packet.data, 3).GetStringFromUtf8();

		if (!Buglib.VerifyHexString(fileId)) // Invalid id
			return;

		if (extension == RequestService.FileExtension.PeerData){
			// if (fileId == userService.userId) // Ignore our own availability packet
			// 	return;
			
			peerService.AddTemporaryPeer(fileId);
		}

		if (userService.allowService){
			requestService.Request(fileId, extension, verifyMethod);
		}
	}

	private void ProcessSpaceUpdatePacket(PacketService.Packet packet, bool fromEventChain){
		byte[] initVector = Buglib.ReadLength(packet.data, 1, 16);
		byte[][] packetParts = Buglib.ReadDataSpans(packet.data, 17);
		string keyUsed = packetParts[0].GetStringFromUtf8();

		if (!fromEventChain)
			eventChainService.SaveEvent(packet.data, keyUsed);

		byte[] encryptedSection = packetParts[1];
		byte[] decryptedSection = KeyService.AESDecrypt(encryptedSection, keyService.myKeys[keyUsed], initVector);

		byte[][] dataSpans = Buglib.ReadDataSpans(decryptedSection, 8);

		int authorityCount = BitConverter.ToInt32(decryptedSection, 0);
		int memberCount = BitConverter.ToInt32(decryptedSection, 4);

		string spaceId = dataSpans[0].GetStringFromUtf8();
		string spaceName = dataSpans[1].GetStringFromUtf8();
		string keyId = dataSpans[2].GetStringFromUtf8();
		string ownerId = dataSpans[3].GetStringFromUtf8();
		PeerService.Peer owner = peerService.GetPeer(ownerId);

		int memberOffset = 4;
		HashSet<PeerService.Peer> authorities = new HashSet<PeerService.Peer>();
		for (int i = 0; i < authorityCount; i++){
			string authorityId = dataSpans[memberOffset].GetStringFromUtf8();
			authorities.Add(peerService.GetPeer(authorityId));
			memberOffset++;
		}

		HashSet<PeerService.Peer> members = new HashSet<PeerService.Peer>();
		for (int i = 0; i < memberCount; i++){
			string memberId = dataSpans[memberOffset].GetStringFromUtf8();
			authorities.Add(peerService.GetPeer(memberId));
			memberOffset++;
		}

		spaceService.AddSpace(spaceId, spaceName, keyId, owner, authorities, members);

		// if (retrievingChain){
		// 	eventChainService.LoadEventChain(keyId);
		// }else{
		// 	eventChainService.SaveEvent(packet.data, keyUsed);
		// }
	}

	private void ProcessFileRequest(byte[] packet){
		RequestService.FileExtension extension = (RequestService.FileExtension)packet[1];

		string fileGuid = Buglib.ReadDataSpan(packet, 2).GetStringFromUtf8();
		GD.Print("Recieved file request " + fileGuid);

		byte[] fileData = fileService.GetServableData(fileGuid, extension, out bool success);
		if (!success)
			return;

		byte[][] servePartitions = MakePartitions(fileData, filePacketSize);
		for(int i = 0; i < servePartitions.Length; i++){
			packetService.SendPacket(BuildFilePacket(fileGuid, i, servePartitions.Length, servePartitions[i], packet[1]));
		}
	}

	private void ProcessKeyPackage(byte[] packet){
		GD.Print("Processing space invite");

		byte[][] dataSpans = Buglib.ReadDataSpans(packet, 1);

		string keyId = dataSpans[0].GetStringFromUtf8();
		byte[] encryptedSpaceKey = dataSpans[1];

		bool couldDecrypt = keyService.RSADecrypt(encryptedSpaceKey, out byte[] spaceKey);

		if (couldDecrypt){
			keyService.AddKey(keyId, spaceKey);
		}
	}

	private void ProcessIdentify(byte[] packet){
		byte[][] packetDataSpans = Buglib.ReadDataSpans(packet, 1);

		string guid = packetDataSpans[0].GetStringFromUtf8();
		string username = packetDataSpans[1].GetStringFromUtf8();
		byte[] key = packetDataSpans[2];
		string profilePictureId = null;
		if (packetDataSpans[3].GetStringFromUtf8() != "null"){
			profilePictureId = packetDataSpans[3].GetStringFromUtf8();
		}

		if (!peerService.peers.ContainsKey(guid))
			Send(BuildIdentifyingPacket());

		// if (peerService.AddPeer(guid, username, key, profilePictureId)){
		// 	Send(BuildIdentifyingPacket());
		// }
	}

	private void ProcessMessagePacket(PacketService.Packet packet, bool fromEventChain){
		byte[][] spans = Buglib.ReadDataSpans(packet.data, 19);

		ushort hashNonce =  BitConverter.ToUInt16(Buglib.ReadLength(packet.data, 1, 2));
		byte[] initVector = Buglib.ReadLength(packet.data, 3, 16);

		string keyUsed = spans[0].GetStringFromUtf8();
		if (!fromEventChain)
			eventChainService.SaveEvent(packet.data, keyUsed);

		byte[] encryptedSection = spans[1];

		byte[] decryptedSection = KeyService.AESDecrypt(encryptedSection, keyService.myKeys[keyUsed], initVector);

		byte[][] decryptedSpans = Buglib.ReadDataSpans(decryptedSection, 0);
		string senderGuid = decryptedSpans[0].GetStringFromUtf8();

		if (!fromEventChain)
			notificationService.ProcessNotificationPacket(decryptedSpans[1]);

		MessageComponentFlags messageFlags = (MessageComponentFlags)BitConverter.ToUInt16(decryptedSpans[2]);

		// Read all components of the message based off of present flags
		int readingSpan = 3;
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

		if (messageFlags.HasFlag(MessageComponentFlags.FileEmbed)){
			if (fileService.IsFileInCache(embedId)){
				fileService.EmitSignal(FileService.SignalName.OnCacheChanged, embedId); // Call the embed message ui to update and load the image from cache
				return;
			}

			requestService.Request(embedId, RequestService.FileExtension.MediaFile, RequestService.VerifyMethod.HashCheck);
		}
	}

	#endregion

	#region packet builders

	private byte[] BuildFileAvailabilityPacket(string fileId, RequestService.FileExtension extension, RequestService.VerifyMethod verifyMethod){
		List<byte> packetBytes = new List<byte>
        {
            12,
            (byte)extension,
            (byte)verifyMethod
        };

		packetBytes.AddRange(Buglib.MakeDataSpan(fileId.ToUtf8Buffer()));

		return packetBytes.ToArray();
	}

	private byte[] BuildSpaceUpdatePacket(SpaceService.Space space){
		List<byte> packetBytes = new List<byte>{
			9
		};

		List<byte> sectionToEncrypt = new List<byte>();

		sectionToEncrypt.AddRange(BitConverter.GetBytes(space.authorities.Count));
		sectionToEncrypt.AddRange(BitConverter.GetBytes(space.members.Count));

		sectionToEncrypt.AddRange(Buglib.MakeDataSpan(space.id.ToUtf8Buffer()));
		sectionToEncrypt.AddRange(Buglib.MakeDataSpan(space.name.ToUtf8Buffer()));
		sectionToEncrypt.AddRange(Buglib.MakeDataSpan(space.keyId.ToUtf8Buffer()));

		sectionToEncrypt.AddRange(Buglib.MakeDataSpan(space.owner.id.ToUtf8Buffer()));

		foreach (PeerService.Peer peer in space.authorities){
			sectionToEncrypt.AddRange(Buglib.MakeDataSpan(peer.id.ToUtf8Buffer()));
		}

		foreach (PeerService.Peer peer in space.members){
			sectionToEncrypt.AddRange(Buglib.MakeDataSpan(peer.id.ToUtf8Buffer()));
		}

		byte[] initVector = KeyService.GetRandomBytes(16);
		packetBytes.AddRange(initVector);
		packetBytes.AddRange(Buglib.MakeDataSpan(space.keyId.ToUtf8Buffer()));
		packetBytes.AddRange(Buglib.MakeDataSpan(keyService.EncryptWithSpace(sectionToEncrypt.ToArray(), space.id, initVector)));

		return packetBytes.ToArray();
	}

	private byte[] BuildFilePacket(string fileGuid, int filePart, int totalFileParts, byte[] data, byte subtype){
		List<byte> packetBytes = new List<byte>{
			7,
			subtype,
		};

		packetBytes.AddRange(BitConverter.GetBytes((ushort)filePart));
		packetBytes.AddRange(BitConverter.GetBytes((ushort)totalFileParts));
		packetBytes.AddRange(Buglib.MakeDataSpan(fileGuid.ToUtf8Buffer()));
		packetBytes.AddRange(Buglib.MakeDataSpan(GetClientId().ToUtf8Buffer()));
		packetBytes.AddRange(Buglib.MakeDataSpan(data, true));

		return packetBytes.ToArray();
	}

	public byte[] BuildEventRequest(string eventFileGuid, long afterDate){
		List<byte> packetBytes = new List<byte>{
			6,
			1 // request subtype
		};
		
		packetBytes.AddRange(BitConverter.GetBytes(afterDate));
		packetBytes.AddRange(Buglib.MakeDataSpan(eventFileGuid.ToUtf8Buffer()));

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

		packetBytes.AddRange(Buglib.MakeDataSpan(fileGuid.ToUtf8Buffer()));

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

		sectionToEncrypt.AddRange(Buglib.MakeDataSpan(userService.localPeer.id.ToUtf8Buffer())); // Sender id

		// Notifications
		List<string> peersToNotify = new List<string>();
		foreach (PeerService.Peer peer in spaceService.spaces[selectedSpaceId].members){
			peersToNotify.Add(peer.id);
		}
		sectionToEncrypt.AddRange(Buglib.MakeDataSpan(notificationService.BuildNotificationPacket(NotificationService.NotificationType.Single, peersToNotify)));
		
		sectionToEncrypt.AddRange(Buglib.MakeDataSpan(BitConverter.GetBytes((ushort)messageFlags))); // Message flags
		if (text != null)
			sectionToEncrypt.AddRange(Buglib.MakeDataSpan(text.ToUtf8Buffer()));
		if (embedId != null)
			sectionToEncrypt.AddRange(Buglib.MakeDataSpan(embedId.ToUtf8Buffer()));
		if (replyingTo != null)
			sectionToEncrypt.AddRange(Buglib.MakeDataSpan(replyingTo.ToUtf8Buffer()));

		byte[] encryptedSection = keyService.EncryptWithSpace(sectionToEncrypt.ToArray(), selectedSpaceId, initVector);

		packetList.AddRange(hashNonce);
		packetList.AddRange(initVector);
		packetList.AddRange(Buglib.MakeDataSpan(keyId));
		packetList.AddRange(Buglib.MakeDataSpan(encryptedSection));

		return packetList.ToArray();
	}

	private byte[] BuildIdentifyingPacket(){
		if (!userService.identifySelf)
			return System.Array.Empty<byte>();

		List<byte> packetBytes = new List<byte>{
			1
		};

		byte[] publicKey = keyService.GetPublicKey();
		byte[] username = userService.localPeer.username.ToUtf8Buffer();
		byte[] guid = userService.localPeer.id.ToUtf8Buffer();
		byte[] profilePicture = "null".ToUtf8Buffer();
		if (userService.localPeer.profilePictureId != null && userService.localPeer.profilePictureId.Length > 0){
			profilePicture = userService.localPeer.profilePictureId.ToUtf8Buffer();
		}
		
		packetBytes.AddRange(Buglib.MakeDataSpan(guid));
		packetBytes.AddRange(Buglib.MakeDataSpan(username));
		packetBytes.AddRange(Buglib.MakeDataSpan(publicKey));
		packetBytes.AddRange(Buglib.MakeDataSpan(profilePicture));

		return packetBytes.ToArray();
	}

	private byte[] BuildKeyPackage(string recipientId, string keyId){
		List<byte> packetBytes = new List<byte>{
			4
		};

		byte[] spaceKeyEncrypted = keyService.EncryptKeyForPeer(keyId, recipientId);

		packetBytes.AddRange(Buglib.MakeDataSpan(keyId.ToUtf8Buffer()));
		packetBytes.AddRange(Buglib.MakeDataSpan(spaceKeyEncrypted));

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
}
