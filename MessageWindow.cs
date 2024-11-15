using Godot;
using System;
using System.Collections.Generic;

public partial class MessageWindow : Control
{
	[Export] public PackedScene messageScene;
	[Export] public VBoxContainer messageContainer;
	[Export] public ScrollContainer scrollContainer;

	private VScrollBar scrollBar;
	private bool atBottom;
	private double maxScroll;

	private RequestService requestService;
	private FileService fileService;
	private DatabaseService databaseService;
	private PeerService peerService;

	private List<MessageUI> displayedMessageUIs = new List<MessageUI>();
	private Dictionary<string, DatabaseService.Message> displayingMessages = new Dictionary<string, DatabaseService.Message>();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		requestService = GetNode<RequestService>("/root/Main/Bugcord/RequestService");
		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
		databaseService = GetNode<DatabaseService>("/root/Main/Bugcord/DatabaseService");
		peerService = GetNode<PeerService>("/root/Main/Bugcord/PeerService");

		scrollBar = scrollContainer.GetVScrollBar();
		scrollBar.Changed += OnContentAdded;
		maxScroll = scrollBar.MaxValue;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void DisplaySpaceMessages(string spaceId, string spaceName){
		// Clear all messages
		foreach (MessageUI message in displayedMessageUIs){
			message.QueueFree();
		}
		displayedMessageUIs.Clear();
		displayingMessages.Clear();

		// List<DatabaseService.Message> messages = databaseService.GetMessages(spaceId);

		// for (int i = 0; i < messages.Count; i++){
		// 	DisplayNewMessage(messages[i]);
		// }
	}

	public void DisplayNewMessage(Godot.Collections.Dictionary messageDict){
		DisplayNewMessage((DatabaseService.Message)messageDict);
	}

	public void DisplayNewMessage(DatabaseService.Message message){
		atBottom = IsScrollAtBottom();

		string replyPreview = null;
		if (message.replyingTo != null && displayingMessages.ContainsKey(message.replyingTo)){
			DatabaseService.Message replyingToMessage = displayingMessages[message.replyingTo];
			replyPreview = peerService.peers[replyingToMessage.senderId].username;
			replyPreview += ": " + replyingToMessage.content;
		}

		MessageUI newMessage = messageScene.Instantiate<MessageUI>();
		newMessage.Initiate(message, replyPreview, fileService, peerService);
		messageContainer.AddChild(newMessage);

		if (message.embedId != null && message.embedId != ""){
			bool gotImmedietly = fileService.GetFile(message.embedId, out byte[] data);

			if (gotImmedietly){ // File is in the cache right now. No waiting needed
				newMessage.CacheUpdated(message.embedId);
			}else{
				fileService.OnCacheChanged += newMessage.CacheUpdated;
				requestService.OnRequestProgress += newMessage.FileBufferUpdated;
			}
		}

		displayedMessageUIs.Add(newMessage);
		displayingMessages.Add(message.id, message);
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
