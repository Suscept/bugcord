using Godot;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

public partial class SpaceService : Node
{
	[Export] public SpacesList spaceDisplay;

	// Space Id, Space Data
	public Dictionary<string, Dictionary<string, string>> spaces = new();

	public const string clientSpacesPath = "user://spaces.json";

	private KeyService keyService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		keyService = GetParent().GetNode<KeyService>("KeyService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void AddSpace(string spaceId, string spaceName, string keyId){
		if (spaces.ContainsKey(spaceId)){
			GD.Print("client already in space");
			return;
		}

		Dictionary<string, string> spaceData = new(){
			{"name", spaceName},
			{"keyId", keyId}
		};

		spaces.Add(spaceId, spaceData);
	}

	public void GenerateSpace(string name){
		string keyGuid = keyService.NewKey();

		Dictionary<string, string> spaceData = new(){
			{"name", name},
			{"keyId", keyGuid}
		};

		spaces.Add(Guid.NewGuid().ToString(), spaceData);
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
		foreach (KeyValuePair<string, Dictionary<string, string>> entry in spaces){
			Godot.Collections.Dictionary entryData = new();
			foreach (KeyValuePair<string, string> dataEntry in entry.Value){
				entryData.Add(dataEntry.Key, dataEntry.Value);
			}
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
			Dictionary<string, string> entryData = new();
			foreach (KeyValuePair<Variant, Variant> dataEntry in (Godot.Collections.Dictionary)entry.Value){
				entryData.Add((string)dataEntry.Key, (string)dataEntry.Value);
			}
			spaces.Add((string)entry.Key, entryData);
		}
		userSpaces.Close();

		spaceDisplay.Update(spaces);
	}
}
