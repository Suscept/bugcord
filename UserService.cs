using Godot;
using Godot.Collections;
using System;
using System.Security.Cryptography;

public partial class UserService : Node
{
	[Signal] public delegate void OnLoggedInEventHandler();

	public string userId;
	public string userName;
	public string savedServerIp;
	public string profilePictureFileId;
	public bool autoConnectToServer;
	public bool identifySelf = true; // Controls if the client will automatically send an identifying packet to the network
	public bool allowService = true; // If false, the client will not store data for others. Keep true plz
	public int serviceAllowance = 5000; // The amount in megabytes to use for providing service to other peers.
	public string customServicePath;

	public const string clientSavePath = "user://client.data";

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void MakeNewUser(string username, string password){
		userId = Guid.NewGuid().ToString();
		userName = username;
	}

	public void SaveToFile(){
		Dictionary userDict = new Dictionary{
			{"id", userId},
			{"username", userName },
			{"defaultConnectServer", savedServerIp},
			{"autoConnectToServer", autoConnectToServer},
			{"profilePictureFileId", profilePictureFileId},
			{"identifySelf", identifySelf},
			{"allowService", allowService},
			{"serviceAllowance", serviceAllowance},
			{"customServicePath", customServicePath},
		};

		FileAccess userFile = FileAccess.Open(clientSavePath, FileAccess.ModeFlags.Write);
		userFile.Seek(0);
		userFile.StoreLine(Json.Stringify(userDict));
		userFile.Close();
	}

	public void LoadFromFile(){
		FileAccess userData = FileAccess.Open(clientSavePath, FileAccess.ModeFlags.Read);
		string userfileRaw = userData.GetAsText();
		GD.Print("user: " + userfileRaw);
		Variant userParsed = Json.ParseString(userfileRaw);
		Dictionary userDict = (Dictionary)userParsed.Obj;

		userId = (string)userDict["id"];
		userName = (string)userDict["username"];
		savedServerIp = (string)userDict["defaultConnectServer"];

		if (userDict.ContainsKey("profilePictureFileId"))
			profilePictureFileId = (string)userDict["profilePictureFileId"];
		
		autoConnectToServer = (bool)userDict["autoConnectToServer"];

		if (userDict.ContainsKey("identifySelf"))
			identifySelf = (bool)userDict["identifySelf"];

		if (userDict.ContainsKey("allowService"))
			allowService = (bool)userDict["allowService"];
		if (userDict.ContainsKey("serviceAllowance"))
			serviceAllowance = (int)userDict["serviceAllowance"];
		if (userDict.ContainsKey("customServicePath"))
			customServicePath = (string)userDict["customServicePath"];
	}
}
