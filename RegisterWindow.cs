using Godot;
using System;

public partial class RegisterWindow : Panel
{
	[Export] public LineEdit usernameInput;
	[Export] public LineEdit passwordInput;
	[Export] public LineEdit repeatPasswordInput;

	[Signal] public delegate void OnRegisterEventHandler(string username, string password);

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void AttemptRegister(){
		if (usernameInput.Text.Length <= 0){
			return;
		}

		if (passwordInput.Text.Length <= 0){
			return;
		}

		if (passwordInput.Text != repeatPasswordInput.Text){
			return;
		}

		Visible = false;
		EmitSignal(SignalName.OnRegister, usernameInput.Text, passwordInput.Text);
	}
}
