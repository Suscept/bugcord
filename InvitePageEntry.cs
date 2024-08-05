using Godot;
using System;

public partial class InvitePageEntry : MarginContainer
{
	[Signal] public delegate void OnEntryInvitedEventHandler(string userGuid);

	[Export] Label usernameTextObject;
	[Export] TextureRect profilePicture;

	public string displayText;

	private string myGuid;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void Initialize(string guid, string username){
		usernameTextObject.Text = displayText = username;
		myGuid = guid;
	}

	public void Invite(){
		EmitSignal(SignalName.OnEntryInvited, myGuid);
	}
}
