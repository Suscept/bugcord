using Godot;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;

public partial class SpaceService : Node
{
	[Export] public SpacesList spaceDisplay;

	// Space Id, Space Data
	// public Dictionary<string, Dictionary<string, string>> spaces = new();

	public Dictionary<string, Space> spaces = new();

	public const string clientSpacesPath = "user://spaces.json";
	public const string spaceServePath = "user://serve/";

	private UserService userService;
	private PeerService peerService;
	private KeyService keyService;
	private DatabaseService databaseService;
	private Bugcord bugcord;

	// Key id, space id
	private Dictionary<string, string> keyUsage = new();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		userService = GetParent().GetNode<UserService>("UserService");
		peerService = GetParent().GetNode<PeerService>("PeerService");
		keyService = GetParent().GetNode<KeyService>("KeyService");
		databaseService = GetParent().GetNode<DatabaseService>("DatabaseService");
		bugcord = GetParent<Bugcord>();
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

	public void OnUpdateSpace(Space space, string prevKey){
		GD.Print("Space updated. Changing key..");
		if (space.keyId != prevKey){ // Update key
			keyUsage.Remove(prevKey);
			keyUsage.Add(space.keyId, space.id);
		}

		spaceDisplay.Update(spaces);
		SaveToFile();

		bugcord.UpdateSpace(space.id);
	}

	public void AddSpace(string spaceId, string spaceName, string spaceKeyId, PeerService.Peer owner, HashSet<PeerService.Peer> authorities, HashSet<PeerService.Peer> members){
		Space spaceData = new(){
			id = spaceId,
			name = spaceName,
			keyId = spaceKeyId,
			owner = owner,
			authorities = authorities,
			members = members,
		};

		AddSpace(spaceData);
	}

	public void AddSpace(Space space){
		if (spaces.ContainsKey(space.id)){
			GD.Print("Updating space");

			// Remove old key if changed
			if (spaces[space.id].keyId != space.keyId){
				keyUsage.Remove(spaces[space.id].keyId);
				keyUsage.Add(space.keyId, space.id);
			}

			spaces[space.id].name = space.name;
			spaces[space.id].keyId = space.keyId;
			spaces[space.id].owner = space.owner;
			spaces[space.id].authorities = space.authorities;
			spaces[space.id].members = space.members;

			spaceDisplay.Update(spaces);
			SaveToFile();

			return;
		}

		spaces.Add(space.id, space);
		keyUsage.Add(space.keyId, space.id);

		spaceDisplay.Update(spaces);
		SaveToFile();
	}

	public void GenerateSpace(string name){
		string keyGuid = keyService.NewKey();
		string spaceId = keyGuid;

		HashSet<PeerService.Peer> spaceAuthorites = new HashSet<PeerService.Peer>
        {
            peerService.GetLocalPeer()
        };
		HashSet<PeerService.Peer> spaceMembers = new HashSet<PeerService.Peer>
        {
            peerService.GetLocalPeer()
        };

		Space newSpace = new Space(){
			name = name,
			keyId = keyGuid,
			id = spaceId,
			owner = peerService.GetLocalPeer(),
			authorities = spaceAuthorites,
			members = spaceMembers
		};

		AddSpace(newSpace);
	}

	public void SaveToFile(Space space){
		FileAccess spaceFile = FileAccess.Open(spaceServePath + space.id + ".space", FileAccess.ModeFlags.Write);
		spaceFile.StoreString(JsonConvert.SerializeObject(space));
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

			Godot.Collections.Array members = new Godot.Collections.Array();
			foreach (PeerService.Peer member in entry.Value.members){
				members.Add(member.id);
			}
			
			Godot.Collections.Dictionary entryData = new()
            {
                { "name", entry.Value.name },
                { "keyId", entry.Value.keyId },
                { "owner", entry.Value.owner.id },
                { "authorities", authorities },
                { "members", members },
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
		GD.Print("SpaceService: Loading from file");
		if (!FileAccess.FileExists(clientSpacesPath)){
			GD.Print("- No space file found");
			return;
		}

		FileAccess userSpaces = FileAccess.Open(clientSpacesPath, FileAccess.ModeFlags.Read);
		string spaceFileRaw = userSpaces.GetAsText();

		// Convert godot dictionary to system dictionary
		foreach (KeyValuePair<Variant, Variant> entry in (Godot.Collections.Dictionary)Json.ParseString(spaceFileRaw)){
			Godot.Collections.Dictionary spaceInfo = (Godot.Collections.Dictionary)entry.Value;
			if (!spaceInfo.ContainsKey("owner") || !spaceInfo.ContainsKey("authorities")){ // Old space
				continue;
			}

			HashSet<PeerService.Peer> members = new HashSet<PeerService.Peer>();
			if (spaceInfo.ContainsKey("members")){
				Godot.Collections.Array membersJson = (Godot.Collections.Array)spaceInfo["members"];
				foreach (string memberId in membersJson){
					PeerService.Peer member = peerService.GetPeer(memberId);
					if (!members.Contains(member))
						members.Add(member);
				}
			}else{
				members.Add(peerService.GetLocalPeer());
			}

			Godot.Collections.Array authoritiesJson = (Godot.Collections.Array)spaceInfo["authorities"];
			HashSet<PeerService.Peer> authorities = new HashSet<PeerService.Peer>();
			foreach (string authorityId in authoritiesJson){
				PeerService.Peer authority = peerService.GetPeer(authorityId);
				if (!authorities.Contains(authority))
					authorities.Add(authority);
			}

			PeerService.Peer spaceOwner = peerService.GetPeer((string)spaceInfo["owner"]);

			// Broken space fixes
			if (!members.Contains(spaceOwner)){ // Owner is not a member??
				GD.Print("- Fixed owner that was not a member");
				members.Add(spaceOwner);
			}

			if (!authorities.Contains(spaceOwner)){ // Owner is not an authority??
				GD.Print("- Fixed owner that was not an admin");
				authorities.Add(spaceOwner);
			}

			foreach (PeerService.Peer authority in authorities){ // Any authority must also be a member
				GD.Print("- " + members.Contains(authority));
				if (!members.Contains(authority)){
					GD.Print("- Authority missing as member");
					members.Add(authority);
				}
			}

            Space entryData = new()
            {
                id = (string)entry.Key,
                keyId = (string)spaceInfo["keyId"],
                name = (string)spaceInfo["name"],
				owner = peerService.GetPeer((string)spaceInfo["owner"]),
				authorities = authorities,
				members = members
            };
			spaces.Add((string)entry.Key, entryData);
			keyUsage.Add(entryData.keyId, (string)entry.Key);
		}
		userSpaces.Close();

		spaceDisplay.Update(spaces);
	}

	public class Space{
		public string id;
		public string name;
		public string keyId;

		public PeerService.Peer owner;
		public HashSet<PeerService.Peer> authorities = new HashSet<PeerService.Peer>();
		public HashSet<PeerService.Peer> members = new HashSet<PeerService.Peer>();
	}
}
