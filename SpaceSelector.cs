using Godot;
using System;

public partial class SpaceSelector : MarginContainer
{
	[Export] public Label label;

	[Signal] public delegate void OnPickedEventHandler(string spaceGuid);
	[Signal] public delegate void OnInviteEventHandler(string spaceGuid);

	public string spaceGuid;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void Initialize(string guid, string name){
		label.Text = name;
		spaceGuid = guid;
	}

	public void OnSelect(){
		EmitSignal(SignalName.OnPicked, spaceGuid);
	}

	public void OnInviteClick(){
		EmitSignal(SignalName.OnInvite, spaceGuid);
	}
}
