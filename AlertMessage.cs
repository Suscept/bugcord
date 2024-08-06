using Godot;
using System;

public partial class AlertMessage : MarginContainer
{
	[Export] public Label header;
	[Export] public Label subHeader;

	[Export] public RichTextLabel content;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void Initiate(string headerText, string subHeaderText, string contentText){
		subHeader.Visible = subHeaderText.Length > 0;

		header.Text = headerText;
		subHeader.Text = subHeaderText;
		content.Text = contentText;
	}
}
