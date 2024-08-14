using Godot;
using Godot.Collections;
using System;
using System.Security.Cryptography;

public partial class UserService : Node
{
	[Signal] public delegate void OnLoggedInEventHandler();

	// User config
	public string userName;
	public string savedServerIp;
	public bool autoConnectToServer;
	public string webhookUrl;

	public string userId;

	public const string clientSavePath = "user://client.data";
	public const string defaultServerIp = "75.71.255.149:25987";

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
		savedServerIp = defaultServerIp;
		autoConnectToServer = false;
	}

	public void SaveToFile(){
		Dictionary userDict = new Dictionary{
			{"id", userId},
			{"username", userName },
			{"defaultConnectServer", savedServerIp},
			{"autoConnectToServer", autoConnectToServer}
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
		autoConnectToServer = (bool)userDict["autoConnectToServer"];
	}
}
