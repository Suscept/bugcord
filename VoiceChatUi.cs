using Godot;
using System;

public partial class VoiceChatUi : MarginContainer
{
	[Export] public Button connectButton;
	[Export] public Button muteButton;
	[Export] public Button deafenButton;

	[Export] public string connectText = "Connect";
	[Export] public string disconnectText = "Disconnect";

	[Export] public string muteText = "Mute";
	[Export] public string unmuteText = "Un-Mute";

	[Export] public string deafenText = "Deafen";
	[Export] public string undeafenText = "Un-Deafen";

	private bool connected;
	private bool muted;
	private bool deafened;

	private StreamService streamService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		streamService = GetNode<StreamService>("/root/Main/Bugcord/StreamService");
	}

	public void OnConnectButtonPressed(){
		connected = !connected;

		UpdateButtonTexts();
		UpdateVoiceState();
	}

	public void OnMuteButtonPressed(){
		muted = !muted;

		UpdateButtonTexts();
		UpdateVoiceState();
	}

	public void OnDeafenButtonPressed(){
		deafened = !deafened;
		muted = true;

		UpdateButtonTexts();
		UpdateVoiceState();
	}

	private void UpdateVoiceState(){
		streamService.sendVoice = connected && !muted;
		streamService.recieveVoice = connected && !deafened;
	}

	private void UpdateButtonTexts(){
		if (connected){
			connectButton.Text = disconnectText;
		}else{
			connectButton.Text = connectText;
		}

		if (muted){
			muteButton.Text = unmuteText;
		}else{
			muteButton.Text = muteText;
		}

		if (deafened){
			muted = true;
			deafenButton.Text = undeafenText;
		}else{
			deafenButton.Text = deafenText;
		}
	}
}
