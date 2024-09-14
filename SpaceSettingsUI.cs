using Godot;
using System;
using System.Collections.Generic;

public partial class SpaceSettingsUI : Panel
{
	[Export] public PackedScene userDisplayUi;
	[Export] public Label spaceTitle;
	[Export] public Label ownerLabel;
	[Export] public RichTextLabel keyIdLabel;

	[Export] public Control adminListContainer;
	[Export] public Control memberListContainer;

	private SpaceService.Space displayingSpace;
	private string displayingId;

	private SpaceService spaceService;
	private PeerService peerService;
	private KeyService keyService;
	private Bugcord bugcord;

	private List<Control> adminUserUis = new List<Control>();
	private List<Control> memberUserUis = new List<Control>();

	// Change store
	private bool changeMade;
	private string spaceNameChange;
	private PeerService.Peer ownerChange;
	private List<PeerService.Peer> invitedPeers = new List<PeerService.Peer>();
	private List<PeerService.Peer> kickedPeers = new List<PeerService.Peer>();
	private List<PeerService.Peer> promotedPeers = new List<PeerService.Peer>();
	private List<PeerService.Peer> demotedPeers = new List<PeerService.Peer>();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		spaceService = GetNode<SpaceService>("/root/Main/Bugcord/SpaceService");
		peerService = GetNode<PeerService>("/root/Main/Bugcord/PeerService");
		keyService = GetNode<KeyService>("/root/Main/Bugcord/KeyService");
		bugcord = GetNode<Bugcord>("/root/Main/Bugcord");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void CancelChanges(){
		spaceNameChange = null;
		ownerChange = null;
		changeMade = false;
		invitedPeers.Clear();
		kickedPeers.Clear();
		promotedPeers.Clear();
		demotedPeers.Clear();
	}

	public void DisplaySpace(string spaceId){
		CancelChanges();

		Visible = true;

		displayingSpace = spaceService.spaces[spaceId];

		spaceTitle.Text = displayingSpace.name;

		ownerLabel.Text = displayingSpace.owner.username;

		keyIdLabel.Text = "Key ID: " + displayingSpace.keyId;

		// User lists
		UpdateUserLists();
	}

	public void SaveUpdate(){
		if (!changeMade){
			return;
		}

		// Refresh key
		string prevKey = displayingSpace.keyId;
		displayingSpace.keyId = keyService.NewKey(); // Change the space's key

		// Change name
		if (spaceNameChange != null){
			displayingSpace.name = spaceNameChange;
		}

		// Add new members
		displayingSpace.members.AddRange(invitedPeers);

		// Remove kicked members
		foreach (PeerService.Peer peer in kickedPeers)
		{
			displayingSpace.members.Remove(peer);
		}

		// Demote authorites
		foreach (PeerService.Peer peer in demotedPeers)
		{
			displayingSpace.authorities.Remove(peer);
		}

		// Promote authorities
		displayingSpace.authorities.AddRange(promotedPeers);

		// Change owner
		displayingSpace.owner = ownerChange;

		spaceService.OnUpdateSpace(displayingSpace, prevKey); // Also invites all peers in members list
	}

	public void SpaceNameChangeSubmitted(string newName){
		spaceTitle.Text = newName;
		spaceNameChange = newName;

		changeMade = true;
	}

	private void UpdateUserLists(){
		foreach (Control user in adminUserUis)
		{
			user.QueueFree();
		}
		adminUserUis.Clear();

		foreach (Control user in memberUserUis)
		{
			user.QueueFree();
		}
		memberUserUis.Clear();

		string[] adminActions = new string[]{
			"Demote",
			"Make owner",
		};
		DisplayPeerlist(displayingSpace.authorities, adminActions, adminListContainer, adminUserUis, 0);
		DisplayPeerlist(promotedPeers, adminActions, adminListContainer, adminUserUis, 0);

		string[] memberActions = new string[]{
			"Kick",
			"Make admin",
		};
		DisplayPeerlist(displayingSpace.members, memberActions, memberListContainer, memberUserUis, 2);
		DisplayPeerlist(invitedPeers, memberActions, memberListContainer, memberUserUis, 2);
	}

	public void DisplayPeerlist(List<PeerService.Peer> peers, string[] actions, Control container, List<Control> controlList, int actionIdOffset){
		foreach (PeerService.Peer peer in peers){
			UserDisplayUI userDisplay = userDisplayUi.Instantiate<UserDisplayUI>();
			controlList.Add(userDisplay);
			container.AddChild(userDisplay);
			userDisplay.Initiate(peer, actions, actionIdOffset);
			userDisplay.OnActionPicked += UserActionPressed;
		}
	}

	public void UserActionPressed(string userId, long actionId){
		GD.Print("User action pressed: " + actionId);
		PeerService.Peer refrencedPeer = peerService.GetPeer(userId);
		switch (actionId)
		{
			case 0: // Demote admin
				if (displayingSpace.owner == refrencedPeer) // Disallow demoting the owner
					break;
			
				demotedPeers.Add(refrencedPeer);
				promotedPeers.Remove(refrencedPeer);
				break;
			case 1: // Make Admin Owner
				ownerChange = refrencedPeer;
				ownerLabel.Text = refrencedPeer.username;
				break;
			case 2: // Kick Member
				if (displayingSpace.owner == refrencedPeer) // Disallow kicking the owner
					break;
				kickedPeers.Add(refrencedPeer);
				demotedPeers.Add(refrencedPeer);

				// A peer cannot be invited or promoted and be kicked
				promotedPeers.Remove(refrencedPeer);
				invitedPeers.Remove(refrencedPeer);
				break;
			case 3: // Make Member Admin
				if (displayingSpace.authorities.Contains(refrencedPeer)) // Avoid making a user admin twice
					break;
				promotedPeers.Add(refrencedPeer);
				demotedPeers.Remove(refrencedPeer);
				break;
		}

		changeMade = true;
		UpdateUserLists();
	}

	public void InviteUser(string spaceId, string peerId){
		changeMade = true;
		invitedPeers.Add(peerService.GetPeer(peerId));
		UpdateUserLists();
	}
}
