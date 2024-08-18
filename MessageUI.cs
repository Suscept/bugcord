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

	public void Initiate(DatabaseService.Message message, PeerService peerService){
		usernameLabel.Text = peerService.peers[message.senderId].username;

		if (message.content != null && message.content.Length > 0){
			SetupMessageContent(message.content);
		}

		if (message.embedId != null && message.embedId.Length > 0){
			waitingForEmbedGuid = message.embedId;
		
			mediaLoadingProgressLabel.Text = "Loading...";
		}else{
			imageContent.Visible = false;
			mediaLoadingProgressLabel.Visible = false;
		}
	}

	public void SetupMessageContent(string text){
		string[] lines = text.Split('\n');
		string processedLines = "";
		for (int i = 0; i < lines.Length; i++){
			if (lines[i][0] == '>'){ // Greentext
				lines[i] = lines[i].Insert(0, "[color=green]") + "[/color]";
			}

			if (i != lines.Length - 1){
				lines[i] += '\n';
			}
			processedLines += lines[i];
		}
		textContent.Text = processedLines;
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
