using Godot;
using System;
using System.Collections.Generic;

public partial class PeerService : Node
{
	// Peer id, Peer Data
	public Dictionary<string, Peer> peers = new();

	public const string clientPeerPath = "user://peers.json";

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
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
                {"username", entry.Value.id},
                {"rsapublickey", Bugcord.ToBase64(entry.Value.rsaKey)}
            };

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
			Peer loadPeer = new(){
				id = (string)peer.Key,
				username = (string)((Godot.Collections.Dictionary)peer.Value)["username"],
				rsaKey = Bugcord.FromBase64((string)((Godot.Collections.Dictionary)peer.Value)["rsapublickey"])
			};
			peers.Add((string)peer.Key, loadPeer);
		}
		userPeers.Close();
	}

	public class Peer{
		public string id;
		public string username;
		public byte[] rsaKey;
	}
}
