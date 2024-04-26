using Godot;
using System;

public partial class MessageWindow : Control
{
	[Export] public PackedScene messageScene;
	[Export] public VBoxContainer messageContainer;
	[Export] public ScrollContainer scrollContainer;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		
	}

	public void DisplayNewMessage(string message){
		VScrollBar vScroll = scrollContainer.GetVScrollBar();

		MessageUI newMessage = messageScene.Instantiate<MessageUI>();
		newMessage.Initiate(message, "globber", null);
		messageContainer.AddChild(newMessage);

		int trueMax = (int)(vScroll.MaxValue - vScroll.Page);
		//scrollContainer.ScrollVertical = trueMax;
		// idk bruh
	}
}
