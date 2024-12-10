using Godot;
using System;

public partial class SettingsPanel : MarginContainer
{
	[Export] public LineEdit usernameSetting;
	[Export] public TextureRect profilePictureDisplay;
	[Export] public FileDialog profilePictureDialog;
	[Export] public LineEdit profileBlurbEdit;
	[Export] public TextEdit profileDescriptionEdit;

	private string pickedProfileImageId;
	private FileService fileService;
	private UserService userService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{	
		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
		userService = GetNode<UserService>("/root/Main/Bugcord/UserService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void OnLoggedIn(){
		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
		userService = GetNode<UserService>("/root/Main/Bugcord/UserService");

		if (userService.localPeer.profilePictureId != null && userService.localPeer.profilePictureId != ""){
			if (fileService.GetFile(userService.localPeer.profilePictureId, out byte[] data)){
				TryLoadProfilePicture();
			}else{
				fileService.OnCacheChanged += TryLoadProfilePicture;
			}
		}

		usernameSetting.Text = userService.localPeer.username;
	}

	public void Save(){
		userService.localPeer.username = usernameSetting.Text;
		userService.localPeer.profilePictureId = pickedProfileImageId;
		userService.SaveClientConfig();
		userService.SaveLocalPeer();
	}

	public void PickProfileImage(){
		profilePictureDialog.Popup();
	}

	public void OnProfileImagePicked(string dir){
		string profilePictureId = fileService.PrepareFile(dir, false, null);
		pickedProfileImageId = profilePictureId;
		DisplayProfileImage(fileService.GetCachePath(profilePictureId));
	}

	public void TryLoadProfilePicture(){
		DisplayProfileImage(fileService.GetCachePath(userService.localPeer.profilePictureId));
	}

	public void TryLoadProfilePicture(string fileId){ // Called from cache update
		if (fileId != userService.localPeer.profilePictureId)
			return;

		TryLoadProfilePicture();
		fileService.OnCacheChanged -= TryLoadProfilePicture;
	}

	private void DisplayProfileImage(string path){
		Image loadedImage = Image.LoadFromFile(path);
		ImageTexture imageTexture = ImageTexture.CreateFromImage(loadedImage);

		profilePictureDisplay.Texture = imageTexture;
	}
}
