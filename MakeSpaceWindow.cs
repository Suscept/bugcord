using Godot;
using System;

public partial class MakeSpaceWindow : Panel
{
	[Export] LineEdit spaceName;

	[Signal] public delegate void OnCreatingSpaceEventHandler(string spaceName);

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void OnCreateSpace(){
		Visible = false;
		EmitSignal(SignalName.OnCreatingSpace, spaceName.Text);
	}

	public void ToggleVisibility(){
		Visible = !Visible;
	}
}
