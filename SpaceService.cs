using Godot;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

public partial class SpaceService : Node
{
	[Export] public SpacesList spaceDisplay;

	// Space Id, Space Data
	// public Dictionary<string, Dictionary<string, string>> spaces = new();

	public Dictionary<string, Space> spaces = new();

	public const string clientSpacesPath = "user://spaces.json";

	private UserService userService;
	private PeerService peerService;
	private KeyService keyService;
	private DatabaseService databaseService;

	// Key id, space id
	private Dictionary<string, string> keyUsage = new();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		userService = GetParent().GetNode<UserService>("UserService");
		peerService = GetParent().GetNode<PeerService>("PeerService");
		keyService = GetParent().GetNode<KeyService>("KeyService");
		databaseService = GetParent().GetNode<DatabaseService>("DatabaseService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public string GetSpaceUsingKey(string keyId){
		if (!keyUsage.ContainsKey(keyId))
			return null;
		
		return keyUsage[keyId];
	}

	public void AddSpace(string spaceId, string spaceName, string spaceKeyId, PeerService.Peer owner, List<PeerService.Peer> authorities){
		if (spaces.ContainsKey(spaceId)){
			GD.Print("Updating space");

			// Remove old key if changed
			if (spaces[spaceId].keyId != spaceKeyId){
				keyUsage.Remove(spaces[spaceId].keyId);
			}

			spaces[spaceId].name = spaceName;
			spaces[spaceId].keyId = spaceKeyId;
			spaces[spaceId].owner = owner;
			spaces[spaceId].authorities = authorities;

			keyUsage.Add(spaceKeyId, spaceId);

			spaceDisplay.Update(spaces);
			SaveToFile();

			return;
		}

		Space spaceData = new(){
			id = spaceId,
			name = spaceName,
			keyId = spaceKeyId,
			owner = owner,
			authorities = authorities,
		};

		spaces.Add(spaceId, spaceData);
		keyUsage.Add(spaceKeyId, spaceId);
		databaseService.AddSpaceTable(spaceId);

		spaceDisplay.Update(spaces);
		SaveToFile();
	}

	public void GenerateSpace(string name){
		string keyGuid = keyService.NewKey();
		string spaceId = Guid.NewGuid().ToString();

		AddSpace(spaceId, name, keyGuid, peerService.peers[userService.userId], new List<PeerService.Peer>());
	}

	public void SaveToFile(){
		if (!FileAccess.FileExists(clientSpacesPath)){
			FileAccess spaceList = FileAccess.Open(clientSpacesPath, FileAccess.ModeFlags.Write);
			Godot.Collections.Dictionary<string, string> spaceDict = new();
			spaceList.StoreString(Json.Stringify(spaceDict));
			spaceList.Close();
		}

		// Convert system dictionary to godot dictionary
		Godot.Collections.Dictionary spacesToSave = new();
		foreach (KeyValuePair<string, Space> entry in spaces){
			Godot.Collections.Array authorities = new Godot.Collections.Array();
			foreach (PeerService.Peer authority in entry.Value.authorities){
				authorities.Add(authority.id);
			}
			
			Godot.Collections.Dictionary entryData = new()
            {
                { "name", entry.Value.name },
                { "keyId", entry.Value.keyId },
                { "owner", entry.Value.owner.id },
                { "authorities", authorities },
            };
			spacesToSave.Add(entry.Key, entryData);
		}

		FileAccess spaceFile = FileAccess.Open(clientSpacesPath, FileAccess.ModeFlags.Write);
		spaceFile.Seek(0);
		spaceFile.StoreString(Json.Stringify(spacesToSave));
		spaceFile.Close();

		spaceDisplay.Update(spaces);
	}

	public void LoadFromFile(){
		if (!FileAccess.FileExists(clientSpacesPath)){
			return;
		}

		FileAccess userSpaces = FileAccess.Open(clientSpacesPath, FileAccess.ModeFlags.Read);
		string spaceFileRaw = userSpaces.GetAsText();

		// Convert godot dictionary to system dictionary
		foreach (KeyValuePair<Variant, Variant> entry in (Godot.Collections.Dictionary)Json.ParseString(spaceFileRaw)){
			Godot.Collections.Array authoritiesJson = (Godot.Collections.Array)((Godot.Collections.Dictionary)entry.Value)["authorities"];
			List<PeerService.Peer> authorities = new List<PeerService.Peer>();
			foreach (string authorityId in authoritiesJson){
				authorities.Add(peerService.peers[authorityId]);
			}

            Space entryData = new()
            {
                id = (string)entry.Key,
                keyId = (string)((Godot.Collections.Dictionary)entry.Value)["keyId"],
                name = (string)((Godot.Collections.Dictionary)entry.Value)["name"],
				owner = peerService.peers[(string)((Godot.Collections.Dictionary)entry.Value)["owner"]],
				authorities = authorities,
            };
			spaces.Add((string)entry.Key, entryData);
			keyUsage.Add(entryData.keyId, (string)entry.Key);
			databaseService.AddSpaceTable((string)entry.Key);
		}
		userSpaces.Close();

		spaceDisplay.Update(spaces);
	}

	public class Space{
		public string id;
		public string name;
		public string keyId;

		public PeerService.Peer owner;
		public List<PeerService.Peer> authorities = new List<PeerService.Peer>();
	}
}
