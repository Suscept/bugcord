using Godot;
using System;

public partial class ProfileViewer : Panel
{
	[Export] public TextureRect profilePictureRect;
	[Export] public Label usernameLabel;
	[Export] public Label blurbLabel;
	[Export] public RichTextLabel profileTextLablel;

	private PeerService.Peer currentlyDisplayingPeer;

	private PeerService peerService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		peerService = GetNode<PeerService>("/root/Main/Bugcord/PeerService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void ViewPeerProfile(PeerService.Peer peer){
		currentlyDisplayingPeer = peer;

		usernameLabel.Text = peer.username;
		blurbLabel.Text = peer.profileBlurb;
		profileTextLablel.Text = peer.profileText;

		Visible = true;

		SetProfilePicture(peer.profilePictureId);
	}

	public void SetProfilePicture(string fileId){
		if (fileId != currentlyDisplayingPeer.profilePictureId)
			return;

		bool availableNow = peerService.GetProfilePicture(currentlyDisplayingPeer.id, SetProfilePicture, out ImageTexture profileImage);
		if (!availableNow)
			return;

		profilePictureRect.Texture = profileImage;
	}
}
