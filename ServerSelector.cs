using Godot;
using Godot.Collections;
using System;

public partial class ServerSelector : MarginContainer
{
	[Signal] public delegate void OnServerSelectedEventHandler(string serverUrl);
	[Signal] public delegate void OnAutoconnectSettingChangedEventHandler(bool setTrue);

	[Export] public LineEdit serverUrlInput;
	[Export] public CheckBox autoConnectCheckBox;

	private UserService userService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		userService = GetNode<UserService>("/root/Main/Bugcord/UserService");
	}

	public void SelectServer(){
		EmitSignal(SignalName.OnServerSelected, serverUrlInput.Text);
	}

	public void OnLoggedIn(){
		if (userService == null){ // This function is called before _Ready()
			userService = GetNode<UserService>("/root/Main/Bugcord/UserService");
		}

		serverUrlInput.Text = userService.savedServerIp;
		autoConnectCheckBox.ButtonPressed = userService.autoConnectToServer;
	}

	public void AutoConnectCheckChanged(bool setTrue){
		EmitSignal(SignalName.OnAutoconnectSettingChanged, setTrue);
	}
}
