using Godot;
using System;

public partial class MakeVisible : Button
{
	[Export] public Control target;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void SetVisibility(bool visible){
		target.Visible = visible;
	}

	public void ToggleVisibility(){
		SetVisibility(!target.Visible);
	}

	public void SetInvisible(){
		SetVisibility(false);
	}

	public void SetVisible(){
		SetVisibility(true);
	}
}
