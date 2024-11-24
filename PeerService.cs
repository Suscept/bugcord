using Godot;
using System;
using System.Collections.Generic;

public partial class PeerService : Node
{
	[Signal] public delegate void OnProfileImageAvailableEventHandler(ImageTexture profile, string peerId);

	// Peer id, Peer Data
	public Dictionary<string, Peer> peers = new();

	public const string clientPeerPath = "user://peers.json";

	// FileId, PeerId
	private Dictionary<string, string> awaitingProfilePictures = new();

	// PeerId, Image
	private Dictionary<string, ImageTexture> peerProfileImages = new();

	private KeyService keyService;
	private UserService userService;
	private FileService fileService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		keyService = GetParent().GetNode<KeyService>("KeyService");
		userService = GetParent().GetNode<UserService>("UserService");
		fileService = GetParent().GetNode<FileService>("FileService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public Peer GetLocalPeer(){
	}

	public Peer GetPeer(string peerId){
		return peers[peerId];
	}

	public bool AddPeer(string id, string username, byte[] rsaKey, string profilePictureId){
		if (peers.ContainsKey(id)){
			GD.Print("Peer already known. Updating info");
			peers[id].username = username;
			peers[id].profilePictureId = profilePictureId;
			SaveToFile();
			return false;
		}

		GD.Print("Adding new peer");
		
		peers.Add(id, new Peer(){
			id = id,
			username = username,
			profilePictureId = profilePictureId,
		});

		keyService.peerKeys.Add(id, rsaKey);

		SaveToFile();
		return true;
	}

	public void SaveToFile(){
		if (!FileAccess.FileExists(clientPeerPath)){
			FileAccess peerList = FileAccess.Open(clientPeerPath, FileAccess.ModeFlags.Write);
			Godot.Collections.Dictionary<string, string> peerDict = new();
			peerList.StoreString(Json.Stringify(peerDict));
			peerList.Close();
		}

		// Convert to godot dictionary to save
		Godot.Collections.Dictionary savePeers = new();
		foreach (KeyValuePair<string, Peer> entry in peers){
			Godot.Collections.Dictionary savePeer = new(){
                {"username", entry.Value.username},
                {"rsapublickey", Bugcord.ToBase64(keyService.peerKeys[entry.Key])}
            };

			if (entry.Value.profilePictureId != null){
				savePeer.Add("profilePictureId", entry.Value.profilePictureId);
			}

			savePeers.Add(entry.Key, savePeer);
		}

		FileAccess peerFile = FileAccess.Open(clientPeerPath, FileAccess.ModeFlags.Write);
		peerFile.Seek(0);
		peerFile.StoreString(Json.Stringify(savePeers));
		peerFile.Close();
	}

	public void LoadFromFile(){
		if (!FileAccess.FileExists(clientPeerPath)){
			return;
		}

		FileAccess userPeers = FileAccess.Open(clientPeerPath, FileAccess.ModeFlags.Read);
		string peerFileRaw = userPeers.GetAsText();

		// Convert to system dictionary
		Godot.Collections.Dictionary loadPeers = (Godot.Collections.Dictionary)Json.ParseString(peerFileRaw);
		foreach (KeyValuePair<Variant, Variant> peer in loadPeers){
			Godot.Collections.Dictionary peerDict = (Godot.Collections.Dictionary)peer.Value;
			Peer loadPeer = new(){
				id = (string)peer.Key,
				username = (string)peerDict["username"],
			};

			if (peerDict.ContainsKey("profilePictureId")){
				loadPeer.profilePictureId = (string)peerDict["profilePictureId"];
			}

			peers.Add((string)peer.Key, loadPeer);

			keyService.peerKeys.Add((string)peer.Key, Bugcord.FromBase64((string)peerDict["rsapublickey"]));
		}
		userPeers.Close();
	}

	/// <summary>
	/// Gets the peer's profile picture.
	/// </summary>
	/// <param name="peerId">The peer's id</param>
	/// <param name="profileImage">OUT: The peer's profile picture</param>
	/// <param name="fileId">The file id to wait for if the file needs to be pulled from the network. Null if the peer doesnt have a profile picture</param>
	/// <returns>If the image is available now</returns>
	public bool GetProfilePicture(string peerId, out ImageTexture profileImage){
		string imageId = peers[peerId].profilePictureId;
		if (imageId == null){
			profileImage = null;
			return true;
		}

		if (!peerProfileImages.ContainsKey(peerId)){
			bool cachedNow = fileService.GetFile(imageId);
			if (cachedNow){
				string path = fileService.GetCachePath(imageId);
				peerProfileImages.Add(peerId, ImageTexture.CreateFromImage(Image.LoadFromFile(path)));
			}else{
				awaitingProfilePictures.Add(imageId, peerId);

				profileImage = null;
				return false; // Image isnt ready yet :(
			}
		}

		profileImage = peerProfileImages[peerId];
		return true;
	}

	public void OnCacheChanged(string fileId){
		if (!awaitingProfilePictures.ContainsKey(fileId))
			return;

		GetProfilePicture(awaitingProfilePictures[fileId], out ImageTexture profileImage);
		EmitSignal(SignalName.OnProfileImageAvailable, profileImage, awaitingProfilePictures[fileId]);
	}

	public class Peer{
		public string id;
		public string username;
		public string profilePictureId;
	}
}
