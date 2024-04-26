using Godot;
using Godot.Collections;
using System;

public partial class ServerSelector : MarginContainer
{
	[Signal] public delegate void OnServerSelectedEventHandler(string serverUrl);
	[Signal] public delegate void OnAutoconnectSettingChangedEventHandler(bool setTrue);

	[Export] public LineEdit serverUrlInput;
	[Export] public CheckBox autoConnectCheckBox;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void SelectServer(){
		EmitSignal(SignalName.OnServerSelected, serverUrlInput.Text);
	}

	public void SetupFromClientDict(Dictionary client){
		serverUrlInput.Text = (string)client["defaultConnectServer"];
		autoConnectCheckBox.ButtonPressed = (string)client["autoConnectToServer"] == "true";
	}

	public void AutoConnectCheckChanged(bool setTrue){
		EmitSignal(SignalName.OnAutoconnectSettingChanged, setTrue);
	}
}
