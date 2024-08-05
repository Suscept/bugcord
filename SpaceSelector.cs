using Godot;
using System;

public partial class SpaceSelector : MarginContainer
{
	[Export] public Label label;
	[Export] public Control selectionDisplay;
	[Export] public Button inviteButton;

	[Signal] public delegate void OnPickedEventHandler(string spaceGuid, SpaceSelector space);
	[Signal] public delegate void OnInviteEventHandler(string spaceGuid);

	public string spaceGuid;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void Initialize(string guid, string name){
		label.Text = name;
		spaceGuid = guid;
		SetSelected(false);
	}

	public void OnSelect(){
		EmitSignal(SignalName.OnPicked, spaceGuid, this);
	}

	public void OnInviteClick(){
		EmitSignal(SignalName.OnInvite, spaceGuid);
	}

	public void SetSelected(bool makeSelected){
		selectionDisplay.Visible = makeSelected;
		inviteButton.Visible = makeSelected;
	}
}
