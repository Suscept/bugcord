using Godot;
using Godot.Collections;
using System;

public partial class MessageWindow : Control
{
	[Export] public Bugcord bugcord;
	[Export] public PackedScene messageScene;
	[Export] public VBoxContainer messageContainer;
	[Export] public ScrollContainer scrollContainer;

	private VScrollBar scrollBar;
	private bool atBottom;
	private double maxScroll;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		scrollBar = scrollContainer.GetVScrollBar();
		scrollBar.Changed += OnContentAdded;
		maxScroll = scrollBar.MaxValue;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void DisplayNewMessage(Dictionary message){
		atBottom = IsScrollAtBottom();

		MessageUI newMessage = messageScene.Instantiate<MessageUI>();
		newMessage.Initiate(message);
		messageContainer.AddChild(newMessage);
	}

	public void DisplayNewMediaMessage(Dictionary message){
		atBottom = IsScrollAtBottom();

		MessageUI newMessage = messageScene.Instantiate<MessageUI>();
		newMessage.InitiateMediaMode(message);
		messageContainer.AddChild(newMessage);

		newMessage.bugcord = bugcord;
		bugcord.OnEmbedCached += newMessage.CacheUpdated;
		bugcord.OnFileBufferUpdated += newMessage.FileBufferUpdated;
	}
	
	public void OnContentAdded(){
		if (maxScroll == scrollBar.MaxValue) // Needed since this function gets called multiple times for some reason causing unexpected behavior
			return;

		if (atBottom == true){ // Only autoscroll if the viewport is already at the bottom
			ScrollToBottom();
		}
	}

	public void ScrollToBottom(){
		maxScroll = scrollBar.MaxValue;
		scrollContainer.ScrollVertical = (int)maxScroll;
	}

	private bool IsScrollAtBottom(){
		int scrollDiff = (int)scrollBar.MaxValue - (int)scrollContainer.Size.Y;
		return scrollContainer.ScrollVertical == scrollDiff || scrollDiff < 0; // scrollDif will be < 0 when content is smaller than the scroll container
	}
}
