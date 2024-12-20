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
		if (requestService == null)
			requestService = GetNode<RequestService>("/root/Main/Bugcord/RequestService");

		if (!requestService.activeRequests.ContainsKey(id)){
			GD.PrintErr(id + " is not an active request");
			return;
		}

		RequestDebug requestDisplay = requestListing.Instantiate<RequestDebug>();
		listingContainer.AddChild(requestDisplay);

		requestDisplay.Initiate(id, requestService.activeRequests[id].maxWaitTime);
		displayedRequests.Add(id, requestDisplay);
	}

	public void UpdateRequest(string id){
		if (requestService == null)
			requestService = GetNode<RequestService>("/root/Main/Bugcord/RequestService");
		
		if (!displayedRequests.ContainsKey(id))
			return;

		displayedRequests[id].IncrementParts();
	}

	public void RemoveRequest(string id){
		if (requestService == null)
			requestService = GetNode<RequestService>("/root/Main/Bugcord/RequestService");

		if (!displayedRequests.ContainsKey(id))
			return;

		displayedRequests[id].QueueFree();
		displayedRequests.Remove(id);
	}
}
