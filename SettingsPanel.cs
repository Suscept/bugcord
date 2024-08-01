using Godot;
using System;

public partial class SettingsPanel : MarginContainer
{
	[Export] public LineEdit usernameSetting;
	[Export] public TextureRect profilePictureDisplay;
	[Export] public FileDialog profilePictureDialog;

	private string pickedProfileImageDir;
	private FileService fileService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{	
		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");

		string selfPfpId = (string)Bugcord.clientUser["id"] + "-pfp";
		string username = (string)Bugcord.clientUser["username"];
		if (fileService.IsFileInCache(selfPfpId)){
			DisplayProfileImage(fileService.cacheIndex[selfPfpId]);
		}

		usernameSetting.Text = username;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void Save(){
		// if (pickedProfileImageDir != null && pickedProfileImageDir.Length > 0){
		// 	Bugcord.PrepareEmbed(pickedProfileImageDir, (string)Bugcord.clientUser["id"] + "-pfp", false);
		// }

		Bugcord.clientUser["username"] = usernameSetting.Text;
		Bugcord.SaveUser();
	}

	public void PickProfileImage(){
		profilePictureDialog.Popup();
	}

	public void OnProfileImagePicked(string dir){
		pickedProfileImageDir = dir;
		DisplayProfileImage(dir);
	}

	private void DisplayProfileImage(string path){
		Image loadedImage = Image.LoadFromFile(path);
		ImageTexture imageTexture = ImageTexture.CreateFromImage(loadedImage);

		profilePictureDisplay.Texture = imageTexture;
	}
}
