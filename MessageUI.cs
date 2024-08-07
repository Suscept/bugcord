using Godot;
using Godot.Collections;
using System;

public partial class MessageUI : MarginContainer
{
	[Export] public float maxHeightFromImage;

	[Export] public TextureRect imageContent;
	[Export] public RichTextLabel textContent;
	[Export] public Label usernameLabel;
	[Export] public Label mediaLoadingProgressLabel;

	public string waitingForEmbedGuid;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void Initiate(Dictionary message){
		usernameLabel.Text = (string)message["sender"];

		if (message.ContainsKey("content")){
			textContent.Text = (string)message["content"];
		}

		if (message.ContainsKey("mediaId")){
			waitingForEmbedGuid = (string)message["mediaId"];
		
			mediaLoadingProgressLabel.Text = "Loading...";
		}else{
			imageContent.Visible = false;
			mediaLoadingProgressLabel.Visible = false;
		}
	}

	public void CacheUpdated(string guid){
		if (guid != waitingForEmbedGuid)
			return;

		FileService fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
		string cachePath = fileService.cacheIndex[guid];
		SetupMediaUi(cachePath);

		mediaLoadingProgressLabel.Visible = false;
		fileService.OnCacheChanged -= CacheUpdated;
		fileService.OnFileBufferUpdated -= FileBufferUpdated;
	}

	public void FileBufferUpdated(string guid, int fileParts, int filePartsTotal){
		mediaLoadingProgressLabel.Text = "Loading... " + fileParts + "/" + filePartsTotal;
	}

	private void SetupMediaUi(string cachePath){
		Image loadedImage = Image.LoadFromFile(cachePath);
		ImageTexture imageTexture = ImageTexture.CreateFromImage(loadedImage);

		imageContent.Texture = imageTexture;

		imageContent.CustomMinimumSize = new Vector2(0, Mathf.Min(maxHeightFromImage, loadedImage.GetSize().Y));
	}
}
