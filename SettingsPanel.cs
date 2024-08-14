using Godot;
using System;

public partial class SettingsPanel : MarginContainer
{
	[Export] public LineEdit usernameSetting;
	[Export] public TextureRect profilePictureDisplay;
	[Export] public FileDialog profilePictureDialog;
	[Export] public RichTextLabel userIdDisplay;

	private string pickedProfileImageDir;
	private FileService fileService;
	private UserService userService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{	
		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
		userService = GetNode<UserService>("/root/Main/Bugcord/UserService");

		string selfPfpId = userService.userId + "-pfp";
		if (fileService.IsFileInCache(selfPfpId)){
			DisplayProfileImage(fileService.cacheIndex[selfPfpId]);
		}

		usernameSetting.Text = userService.userName;
		userIdDisplay.Text = userService.userId;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void Save(){
		// if (pickedProfileImageDir != null && pickedProfileImageDir.Length > 0){
		// 	Bugcord.PrepareEmbed(pickedProfileImageDir, (string)Bugcord.clientUser["id"] + "-pfp", false);
		// }

		userService.userName = usernameSetting.Text;
		userService.SaveToFile();
	}

	public void PickProfileImage(){
		profilePictureDialog.Popup();
	}

	public void OnProfileImagePicked(string dir){
		pickedProfileImageDir = dir;
		DisplayProfileImage(dir);
	}

	public void OnWebhookAuthChanged(string auth){
		userService.webhookUrl = auth;
	}

	private void DisplayProfileImage(string path){
		Image loadedImage = Image.LoadFromFile(path);
		ImageTexture imageTexture = ImageTexture.CreateFromImage(loadedImage);

		profilePictureDisplay.Texture = imageTexture;
	}
}
