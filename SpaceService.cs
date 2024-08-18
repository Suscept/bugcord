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

	private KeyService keyService;
	private DatabaseService databaseService;
	private Dictionary<string, string> keyUsage = new();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
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

	public void AddSpace(string spaceId, string spaceName, string spaceKeyId){
		if (spaces.ContainsKey(spaceId)){
			GD.Print("client already in space");
			return;
		}

		Space spaceData = new(){
			id = spaceId,
			name = spaceName,
			keyId = spaceKeyId,
		};

		spaces.Add(spaceId, spaceData);
		keyUsage.Add(spaceKeyId, spaceId);
		databaseService.AddSpaceTable(spaceId);
	}

	public void GenerateSpace(string name){
		string keyGuid = keyService.NewKey();
		string spaceId = Guid.NewGuid().ToString();

		Space spaceData = new(){
			id = spaceId,
			name = name,
			keyId = keyGuid,
		};

		spaces.Add(spaceId, spaceData);
		databaseService.AddSpaceTable(spaceId);
		SaveToFile();
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
			Godot.Collections.Dictionary entryData = new()
            {
                { "name", entry.Value.name },
                { "keyId", entry.Value.keyId }
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
            Space entryData = new()
            {
                id = (string)entry.Key,
                keyId = (string)((Godot.Collections.Dictionary)entry.Value)["keyId"],
                name = (string)((Godot.Collections.Dictionary)entry.Value)["name"]
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
	}
}
