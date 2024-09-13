using Godot;
using System;

public partial class UserDisplayUI : MarginContainer
{
	[Signal] public delegate void OnActionPickedEventHandler(string displayUserId, long action);

	[Export] public TextureRect profileTexture;
	[Export] public Label usernameLabel;
	[Export] public MenuButton actionsDropdownButton;

	public PeerService.Peer displayingUser;

	private PopupMenu actionDropdown;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		actionDropdown = actionsDropdownButton.GetPopup();
		actionDropdown.IdPressed += OnActionPressed;
	}

	public void Initiate(PeerService.Peer userToDisplay, string[] actions, int actionIdOffset){
		displayingUser = userToDisplay;
		usernameLabel.Text = userToDisplay.username;

		if (actions.Length == 0){
			actionsDropdownButton.Visible = false;
			return;
		}

		int actionId = actionIdOffset;
		foreach (string action in actions)
		{
			actionDropdown.AddItem(action, actionId); // Action id will be equal to action index plus actionIdOffset
			actionId++;
		}
	}

	public void OnActionPressed(long id){
		EmitSignal(SignalName.OnActionPicked, displayingUser.id, id);
	}
}
