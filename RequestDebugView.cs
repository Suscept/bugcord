using Godot;
using System;
using System.Collections.Generic;

public partial class RequestDebugView : Panel
{
	[Export] public PackedScene requestListing;
	[Export] public Control listingContainer;

	public Dictionary<string, RequestDebug> displayedRequests = new Dictionary<string, RequestDebug>();

	private RequestService requestService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		requestService = GetNode<RequestService>("/root/Main/Bugcord/RequestService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void DisplayRequest(string id){
		RequestDebug requestDisplay = requestListing.Instantiate<RequestDebug>();
		listingContainer.AddChild(requestDisplay);

		requestDisplay.Initiate(id, requestService.activeRequests[id].timeLeft);
	}

	public void UpdateRequest(string id){
		displayedRequests[id].IncrementParts();
	}

	public void RemoveRequest(string id){
		displayedRequests[id].QueueFree();
		displayedRequests.Remove(id);
	}
}
