using Godot;
using System;

public partial class EmbedPreviewer : Panel
{
	[Export] public Label filenameLabel;
	[Export] public TextureRect imagePreview;

	private string displayingFileId;
	private FileService fileService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
	}

	public void PreviewEmbed(string id){
		displayingFileId = id;

		Image loadedImage = Image.LoadFromFile(fileService.GetCachePath(displayingFileId));
		ImageTexture imageTexture = ImageTexture.CreateFromImage(loadedImage);

		imagePreview.Texture = imageTexture;
		filenameLabel.Text = fileService.cacheIndex[id].filename;
		Visible = true;
	}

	public void Download(){
		fileService.DownloadFile(displayingFileId);
	}
}
