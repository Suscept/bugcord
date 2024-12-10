using Godot;
using Godot.Collections;
using System;
using System.Security.Cryptography;

public partial class UserService : Node
{
	[Signal] public delegate void OnLoggedInEventHandler();

	// Local peer
	public PeerService.Peer localPeer;
	// public string userId;
	public string userName = "username";
	public string profilePictureFileId;

	// Client settings
	public string savedServerIp;
	public bool autoConnectToServer;
	public bool identifySelf = true; // Controls if the client will automatically send an identifying packet to the network
	public bool allowService = true; // If false, the client will not store data for others. Keep true plz
	public int serviceAllowance = 5000; // The amount in megabytes to use for providing service to other peers.
	public string customServicePath;

	public const string clientSavePath = "user://client.data";

	private KeyService keyService;
	private PeerService peerService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		keyService = GetParent().GetNode<KeyService>("KeyService");
		peerService = GetParent().GetNode<PeerService>("PeerService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	/// <summary>
	/// Creates a peer object and file for this peer. Overrides the current local peer so be careful.
	/// </summary>
	public void MakePeerFile(){
		string userId = keyService.GetUserIdFromAuth();

		PeerService.Peer peer = new PeerService.Peer(){
			id = userId,
			username = userId.Substring(0, 6),
			publicKey = keyService.GetPublicKey(),
		};

		localPeer = peer;
		SaveLocalPeer();
	}

	public void SaveLocalPeer(){
		peerService.SavePeerFile(localPeer, Time.GetUnixTimeFromSystem());
	}

	/// <summary>
	/// Turns the current user into a peer and saves to a file.
	/// </summary>
	public void MakePeerFromUser(){
		PeerService.Peer peer = new PeerService.Peer(){
			id = keyService.GetUserIdFromAuth(),
			username = "username",
			profilePictureId = profilePictureFileId,
			publicKey = keyService.GetPublicKey(),
		};

		if (peerService.peers.ContainsKey(peer.id)){
			peerService.peers[peer.id] = peer;
		}else{
			peerService.peers.Add(peer.id, peer);
		}

		SaveLocalPeer();
		localPeer = peer;
	}

	public void MakeNewUser(string username, string password){
		// userId = KeyService.GetSHA256HashString(keyService.GetPublicKey()); // User id is the hash of the user's public key
		userName = username;
	}

	public void SaveClientConfig(){
		GD.Print("UserService: Saving user file...");

		Dictionary userDict = new Dictionary{
			// {"id", userId},
			// {"username", userName },
			{"defaultConnectServer", savedServerIp},
			{"autoConnectToServer", autoConnectToServer},
			// {"profilePictureFileId", profilePictureFileId},
			{"identifySelf", identifySelf},
			{"allowService", allowService},
			{"serviceAllowance", serviceAllowance},
			{"customServicePath", customServicePath},
		};

		FileAccess userFile = FileAccess.Open(clientSavePath, FileAccess.ModeFlags.Write);
		userFile.Seek(0);
		userFile.StoreLine(Json.Stringify(userDict));
		userFile.Close();

		// MakePeerFromUser();
	}

	public bool LoadClientConfig(){
		if (!FileAccess.FileExists(clientSavePath))
			return false;

		FileAccess userData = FileAccess.Open(clientSavePath, FileAccess.ModeFlags.Read);
		string userfileRaw = userData.GetAsText();
		GD.Print("user: " + userfileRaw);
		Variant userParsed = Json.ParseString(userfileRaw);
		Dictionary userDict = (Dictionary)userParsed.Obj;

		// userId = (string)userDict["id"];
		// userName = (string)userDict["username"];
		savedServerIp = (string)userDict["defaultConnectServer"];

		autoConnectToServer = (bool)userDict["autoConnectToServer"];

		// if (userDict.ContainsKey("profilePictureFileId"))
		// 	profilePictureFileId = (string)userDict["profilePictureFileId"];
		
		if (userDict.ContainsKey("identifySelf"))
			identifySelf = (bool)userDict["identifySelf"];

		if (userDict.ContainsKey("allowService"))
			allowService = (bool)userDict["allowService"];
		if (userDict.ContainsKey("serviceAllowance"))
			serviceAllowance = (int)userDict["serviceAllowance"];
		if (userDict.ContainsKey("customServicePath"))
			customServicePath = (string)userDict["customServicePath"];

		return true;
	}

	public void LoadFromPeer(PeerService.Peer peer){
		localPeer = peer;
	}
}
