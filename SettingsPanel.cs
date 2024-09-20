using Godot;
using System;

public partial class SettingsPanel : MarginContainer
{
	[Export] public LineEdit usernameSetting;
	[Export] public TextureRect profilePictureDisplay;
	[Export] public FileDialog profilePictureDialog;

	private string pickedProfileImageId;
	private FileService fileService;
	private UserService userService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{	
		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
		userService = GetNode<UserService>("/root/Main/Bugcord/UserService");

		if (userService.profilePictureFileId != null){
			if (fileService.GetFile(userService.profilePictureFileId, out byte[] data)){
				TryLoadProfilePicture();
			}else{
				fileService.OnCacheChanged += TryLoadProfilePicture;
			}
		}

		usernameSetting.Text = userService.userName;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void Save(){
		userService.userName = usernameSetting.Text;
		userService.profilePictureFileId = pickedProfileImageId;
		userService.SaveToFile();
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
		DisplayProfileImage(fileService.GetCachePath(userService.profilePictureFileId));
	}

	public void TryLoadProfilePicture(string fileId){ // Called from cache update
		if (fileId != userService.profilePictureFileId)
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
