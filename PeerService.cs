using Godot;
using System;
using System.Collections.Generic;

public partial class PeerService : Node
{
	public const ushort peerPackageVersion = 1;

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
	private RequestService requestService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		keyService = GetParent().GetNode<KeyService>("KeyService");
		userService = GetParent().GetNode<UserService>("UserService");
		fileService = GetParent().GetNode<FileService>("FileService");
		requestService = GetParent().GetNode<RequestService>("RequestService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public Peer GetLocalPeer(){
		return userService.localPeer;
	}

	public Peer GetPeer(string peerId){
		return GetPeer(peerId, out bool fullPeer);
	}

	public void AddTemporaryPeer(string id){
		peers.TryAdd(id, new Peer(){
			id = id,
			username = id.Substring(0, 6),
		});
	}

	/// <summary>
	/// Gets a peer with a provided id.
	/// </summary>
	/// <param name="peerId">The peer's id.</param>
	/// <param name="fullPeer">Out: If this peer is not yet fully known. We can wait on the request for that peer's full data if needed.</param>
	/// <returns>The peer</returns>
	public Peer GetPeer(string peerId, out bool fullPeer){
		if (peers.ContainsKey(peerId)){
			if (peers[peerId].publicKey != null && peers[peerId].publicKey.Length > 0){ // If public key length is zero then this is a temporary peer object
				fullPeer = true;
				return peers[peerId];
			}
		}

		GD.Print("PeerService: Getting peer: " + peerId);
		bool peerFileAvailable = LoadPeerFile(peerId);
		fullPeer = peerFileAvailable;
		if (!peerFileAvailable){
			requestService.Request(peerId, RequestService.FileExtension.PeerData, RequestService.VerifyMethod.NewestSignature);
			AddTemporaryPeer(peerId);
		}
		
		return peers[peerId];
	}

	/// <summary>
	/// Gets the peer's profile picture.
	/// </summary>
	/// <param name="peerId">The peer's id</param>
	/// <param name="profileImage">OUT: The peer's profile picture</param>
	/// <param name="fileId">The file id to wait for if the file needs to be pulled from the network. Null if the peer doesnt have a profile picture</param>
	/// <returns>If the image is available now</returns>
	public bool GetProfilePicture(string peerId, out ImageTexture profileImage){
		bool result = GetProfilePicture(peerId, null, out ImageTexture image);
		profileImage = image;
		return result;
	}

	/// <summary>
	/// Gets the peer's profile picture.
	/// </summary>
	/// <param name="peerId">The peer's id</param>
	/// <param name="profileImage">OUT: The peer's profile picture</param>
	/// <param name="fileId">The file id to wait for if the file needs to be pulled from the network. Null if the peer doesnt have a profile picture</param>
	/// <returns>If the image is available now</returns>
	public bool GetProfilePicture(string peerId, Action<string> subscription, out ImageTexture profileImage){
		string imageId = peers[peerId].profilePictureId;
		if (imageId == null){
			profileImage = null;
			return true;
		}

		if (!peerProfileImages.ContainsKey(peerId)){
			bool cachedNow = fileService.GetFile(imageId, subscription);
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

	public void SavePeerFile(Peer peer, double timestamp){
		GD.Print("PeerService: Saving peer file " + peer.id);

		List<byte> packageBytes = new List<byte>();
		List<byte> signitureBytes = new List<byte>();

		signitureBytes.AddRange(BitConverter.GetBytes(timestamp));
		
		signitureBytes.AddRange(Buglib.MakeDataSpan(peer.id.ToUtf8Buffer()));
		signitureBytes.AddRange(Buglib.MakeDataSpan(peer.publicKey));
		signitureBytes.AddRange(Buglib.MakeDataSpan(peer.username.ToUtf8Buffer()));

		if (peer.profilePictureId != null && peer.profilePictureId.Length > 0){
			signitureBytes.AddRange(Buglib.MakeDataSpan(peer.profilePictureId.ToUtf8Buffer()));
		}else{
			signitureBytes.AddRange(Buglib.MakeDataSpan("null".ToUtf8Buffer()));
		}

		if (peer.profileBlurb != null && peer.profileBlurb.Length > 0){
			signitureBytes.AddRange(Buglib.MakeDataSpan(peer.profileBlurb.ToUtf8Buffer()));
		}else{
			signitureBytes.AddRange(Buglib.MakeDataSpan("\n".ToUtf8Buffer()));
		}

		if (peer.profileText != null && peer.profileText.Length > 0){
			signitureBytes.AddRange(Buglib.MakeDataSpan(peer.profileText.ToUtf8Buffer()));
		}else{
			signitureBytes.AddRange(Buglib.MakeDataSpan("\n".ToUtf8Buffer()));
		}

		packageBytes.AddRange(BitConverter.GetBytes(peerPackageVersion));
		packageBytes.AddRange(keyService.SignData(signitureBytes.ToArray()));
		packageBytes.AddRange(signitureBytes);

		fileService.WriteToServable(packageBytes.ToArray(), peer.id, RequestService.FileExtension.PeerData);
	}

	public bool LoadPeerFile(string peerId){
		GD.Print("PeerService: Loading peer file " + peerId);

		byte[] peerFile = fileService.GetServableData(peerId, RequestService.FileExtension.PeerData, out bool success);
		if (!success){ // File is not on disk
			GD.Print("- Peer file not available");
			if (userService.localPeer.id == peerId){ // We're supposed to always have our own peerfile
				SavePeerFile(userService.localPeer, Time.GetUnixTimeFromSystem());
				// userService.MakePeerFile();
			}
			return false;
		}

		ushort version = BitConverter.ToUInt16(peerFile, 0);
		if (peerPackageVersion > version){ // Version not supported
			GD.Print("- Loading failed. Incorrect version: " + version + " Supported: " + peerPackageVersion);
			return false;
		}

		byte[] signature = Buglib.ReadLength(peerFile, 2, 256);
		double timestamp = BitConverter.ToDouble(peerFile, 258);

		byte[][] dataspans = Buglib.ReadDataSpans(peerFile, 266);

		string id = dataspans[0].GetStringFromUtf8();
		byte[] publicKey = dataspans[1];
		string username = dataspans[2].GetStringFromUtf8();
		string profilePictureId = dataspans[3].GetStringFromUtf8();

		string profileBlurb = dataspans[4].GetStringFromUtf8();
		string profileText = dataspans[5].GetStringFromUtf8();

		if (profilePictureId == "null"){
			profilePictureId = null;
		}

		if (profileBlurb == "\n")
			profileBlurb = "";
		if (profileText == "\n")
			profileText = "";


		// Verify everything
		if (id != peerId){
			GD.Print("- Loading failed. Id mismatch. Expected: " + peerId + " Got: " + id);
			return false;
		}

		// A peer's id should be the hash of it's public key
		if (KeyService.GetSHA256HashString(publicKey) != id){
			GD.Print("- Loading failed. Key hash mismatch");
			return false;
		}

		// Make sure the package was actually created by this peer
		byte[] signatureSection = Buglib.ReadLengthInfinitely(peerFile, 258);
		if (KeyService.VerifySignature(signatureSection, signature, publicKey) == false){
			GD.Print("- Loading failed. Signature not verified");
			return false;
		}

		// The package was made in the future?????
		// Adds one second to the timestamp incase of slight desyncs is system clocks
		double currentTime = Time.GetUnixTimeFromSystem();
		if (timestamp + 1 > currentTime){
			GD.Print("- Loading failed. Package created in the future. This system's time: " + currentTime + " Package timestamp: " + timestamp);
			return false;
		}

        Peer peer = new Peer
        {
            id = id,
			username = username,
			publicKey = publicKey,
			profilePictureId = profilePictureId,
			profileBlurb = profileBlurb,
			profileText = profileText,
        };

		if (peers.ContainsKey(id)){
			peers[id] = peer;
		}else{
			peers.Add(id, peer);
		}
		GD.Print("- Peer file loaded successfully.");
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
		public byte[] publicKey;
		public string profileBlurb;
		public string profileText;
	}
}
