using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

public partial class SpaceInviter : Panel
{
	[Export] public PackedScene inviteEntry;
	[Export] public Control inviteEntryContainer;

	[Signal] public delegate void OnInvitePeerEventHandler(string spaceGuid, string peerGuid);

	private string viewingSpace;

	private List<InvitePageEntry> inviteEntries = new List<InvitePageEntry>();
	private UserService userService;
	private PeerService peerService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		userService = GetNode<UserService>("/root/Main/Bugcord/UserService");
		peerService = GetNode<PeerService>("/root/Main/Bugcord/PeerService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void Search(string query){
		if (inviteEntries.Count < 2){
			return;
		}

		// Score entries
		int[] scores = new int[inviteEntries.Count];
		for (int i = 0; i < inviteEntries.Count; i++){
			for (int c = 0; c < Mathf.Min(inviteEntries[i].displayText.Length, query.Length); c++){
				if (inviteEntries[i].displayText.ToLower()[c] == query.ToLower()[c]){
					scores[i] ++;
				}
			}
		}

		// Sort entries by score (bubble sort)
		for (int i = 0; i < inviteEntries.Count; i++){
			for (int e = 0; e < inviteEntries.Count - 1 - i; e++){
				if (scores[e] < scores[e + 1]){
					// Swap
					inviteEntryContainer.MoveChild(inviteEntries[e + 1], e);
					InvitePageEntry swap = inviteEntries[e+1];
					inviteEntries[e+1] = inviteEntries[e];
					inviteEntries[e] = swap;
					int scoreSwap = scores[e+1];
					scores[e+1] = scores[e];
					scores[e] = scoreSwap;
				}
			}
		}
	}

	public void UpdateList(){
		UpdateList(viewingSpace);
	}

	public void UpdateList(string spaceGuid){
		viewingSpace = spaceGuid;

		foreach (Control entry in inviteEntries){
			entry.QueueFree();
		}

		inviteEntries.Clear();

		foreach (KeyValuePair<string, PeerService.Peer> peer in peerService.peers){
			if (peer.Value.id == userService.localPeer.id)
				continue;

			InvitePageEntry entry = inviteEntry.Instantiate<InvitePageEntry>();

			inviteEntryContainer.AddChild(entry);
			entry.Initialize(peer.Value.id, peer.Value.username);

			entry.OnEntryInvited += InvitePeer;

			inviteEntries.Add(entry);
		}
	}

	public void InvitePeer(string peer){
		EmitSignal(SignalName.OnInvitePeer, viewingSpace, peer);
	}
}
