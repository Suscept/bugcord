using Godot;
using System;

public partial class RegisterWindow : Panel
{
	[Export] public LineEdit usernameInput;
	[Export] public LineEdit passwordInput;
	[Export] public LineEdit repeatPasswordInput;

	[Export] public LineEdit idInput;
	[Export] public LineEdit loginPasswordInput;

	[Signal] public delegate void OnRegisterEventHandler(string username, string password);
	[Signal] public delegate void OnLoginEventHandler(string id, string password);

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

	public void AttemptLogin(){
		if (idInput.Text.Length < 64){
			return;
		}

		if (loginPasswordInput.Text.Length <= 0){
			return;
		}

		Visible = false;
		EmitSignal(SignalName.OnLogin, idInput.Text, loginPasswordInput.Text);
	}
}
