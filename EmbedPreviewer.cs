using Godot;
using System;
using System.IO;

public partial class EmbedPreviewer : Panel
{
	[Export] public Label filenameLabel;
	[Export] public TextureRect imagePreview;
	[Export] public RichTextLabel binaryPreview;
	[Export] public int bytesToDisplay = 512;

	private string displayingFileId;
	private FileService fileService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
	}

	public void PreviewEmbed(string id){
		displayingFileId = id;
		Visible = true;

		imagePreview.Visible = false;
		binaryPreview.Visible = false;

		FileService.CacheFile cacheFile = fileService.cacheIndex[id];
		FileService.FileType fileType = FileService.GetFileType(Path.GetExtension(cacheFile.filename));

		filenameLabel.Text = cacheFile.filename;

		switch (fileType)
		{
			case FileService.FileType.Image:
				imagePreview.Visible = true;
				Image loadedImage = Image.LoadFromFile(cacheFile.path);
				ImageTexture imageTexture = ImageTexture.CreateFromImage(loadedImage);

				imagePreview.Texture = imageTexture;
				break;
			case FileService.FileType.Binary:
				binaryPreview.Visible = true;
				Godot.FileAccess file = Godot.FileAccess.Open(cacheFile.path, Godot.FileAccess.ModeFlags.Read);
				long displayingBytes = (long)Mathf.Min(file.GetLength(), bytesToDisplay);
				byte[] hexBytes = file.GetBuffer(displayingBytes);
				file.Close();

				binaryPreview.Text = "First " + displayingBytes + " bytes: " + Buglib.BytesToHex(hexBytes, " ").ToUpper();
				break;
		}
	}

	public void Download(){
		fileService.DownloadFile(displayingFileId);
	}
}
