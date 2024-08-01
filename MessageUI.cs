using Godot;
using Godot.Collections;
using System;

public partial class MessageUI : MarginContainer
{
	[Export] public float maxHeightFromImage;

	[Export] public TextureRect imageContent;
	[Export] public RichTextLabel textContent;
	[Export] public Label usernameLabel;

	public string waitingForEmbedGuid;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

	public void Initiate(Dictionary message){
		usernameLabel.Text = (string)message["sender"];

		textContent.Text = (string)message["content"];

		imageContent.Visible = false;
	}

	public void InitiateMediaMode(Dictionary message){
		usernameLabel.Text = (string)message["sender"];

		waitingForEmbedGuid = (string)message["mediaId"];
		
		textContent.Text = "Loading...";
	}

	public void CacheUpdated(string guid){
		if (guid != waitingForEmbedGuid)
			return;

		FileService fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
		string cachePath = fileService.cacheIndex[guid];
		SetupMediaUi(cachePath);

		textContent.Visible = false;
		fileService.OnCacheChanged -= CacheUpdated;
		fileService.OnFileBufferUpdated -= FileBufferUpdated;
	}

	public void FileBufferUpdated(string guid, int fileParts, int filePartsTotal){
		textContent.Text = "Loading... " + fileParts + "/" + filePartsTotal;
	}

	private void SetupMediaUi(string cachePath){
		Image loadedImage = Image.LoadFromFile(cachePath);
		ImageTexture imageTexture = ImageTexture.CreateFromImage(loadedImage);

		imageContent.Texture = imageTexture;

		imageContent.CustomMinimumSize = new Vector2(0, Mathf.Min(maxHeightFromImage, loadedImage.GetSize().Y));
	}
}
