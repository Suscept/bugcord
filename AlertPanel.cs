using Godot;
using System;

public partial class AlertPanel : MarginContainer
{
	[Export] public Control popupWindow;
	[Export] public Control alertContainer;

	[Export] public PackedScene alertMessageScene;

	private static AlertPanel instance;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		instance = this;
	}

	public static void PostAlert(string header, string subheader, string content){
		AlertMessage newAlert = instance.alertMessageScene.Instantiate<AlertMessage>();
		newAlert.Initiate(header, subheader, content);

		instance.alertContainer.AddChild(newAlert);
		instance.alertContainer.MoveChild(newAlert, 0); // Moves alert to the top of the list
	}

	public static void PostAlert(string header, string content){
		PostAlert(header, "", content);
	}

	public static void PostAlert(string content){
		PostAlert("Message", "", content);
	}

	public void ToggleVisible(){
		popupWindow.Visible = !popupWindow.Visible;
	}
}
