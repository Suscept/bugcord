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

	private List<Control> inviteEntries = new List<Control>();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
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

		foreach (var (guid, info) in Bugcord.peers){
			if ((string)guid == (string)Bugcord.clientUser["id"])
				continue;

			InvitePageEntry entry = inviteEntry.Instantiate<InvitePageEntry>();

			inviteEntryContainer.AddChild(entry);
			entry.Initialize((string)guid, (string)((Dictionary)info)["username"]);

			entry.OnEntryInvited += InvitePeer;

			inviteEntries.Add(entry);
		}

		Visible = true;
	}

	public void InvitePeer(string peer){
		EmitSignal(SignalName.OnInvitePeer, viewingSpace, peer);
	}
}
