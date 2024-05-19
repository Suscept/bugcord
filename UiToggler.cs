using Godot;
using System;

public partial class UiToggler : Button
{
	[Signal] public delegate void OnVisibilityEnabledEventHandler();
	[Signal] public delegate void OnVisibilityDisabledEventHandler();
	[Signal] public delegate void OnVisibilityToggledEventHandler(bool toggledOn);

	[Export] public Control target;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void Toggle(){
		Toggle(!target.Visible);
	}

	public void Toggle(bool setVisible){
		target.Visible = setVisible;

		EmitSignal(SignalName.OnVisibilityToggled, setVisible);

		if (setVisible){
			EmitSignal(SignalName.OnVisibilityEnabled);
		}else{
			EmitSignal(SignalName.OnVisibilityDisabled);
		}
	}

	public void SetVisible(){
		Toggle(true);
	}

	public void SetInvisible(){
		Toggle(false);
	}
}
