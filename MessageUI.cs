using Godot;
using Godot.Collections;
using System;

public partial class MessageUI : MarginContainer
{
	[Export] public RichTextLabel messageContentLabel;
	[Export] public Label usernameLabel;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	// public override void _Process(double delta)
	// {
	// }

	public void Initiate(Dictionary message){
		messageContentLabel.Text = (string)message["content"];
		usernameLabel.Text = (string)message["sender"];
	}
}
