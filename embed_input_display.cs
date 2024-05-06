using Godot;
using System;

public partial class embed_input_display : MarginContainer
{
	[Export] public Label filenameLabel;
	[Export] public Label fileSizeLabel;
	[Export] public TextureRect filePreviewTexture;

	[Signal] public delegate void OnRemoveEmbedEventHandler(string path);

	private string myPath;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void Initiate(string filename, string fileSize, string path){
		filenameLabel.Text = filename;
		fileSizeLabel.Text = fileSize;

		myPath = path;
	}

	public void OnRemoveEmbedPressed(){
		EmitSignal(SignalName.OnRemoveEmbed, myPath);
	}
}
