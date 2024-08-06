using Godot;
using System;
using System.Collections.Generic;

public partial class PopupAlert : MarginContainer
{
	[Export] public Label titleLabel;
	[Export] public Label extraTitleLabel;
	[Export] public RichTextLabel subtextLabel;
	[Export] public HSeparator subtextSeperator;
	[Export] public Button confirmButton;

	public List<Alert> alertQueue = new List<Alert>();

	public const string defaultButtonText = "Okay";

	private bool displayingAlert;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void UpdateExtraDisplay(){
		extraTitleLabel.Visible = alertQueue.Count > 0;
		extraTitleLabel.Text = "+" + alertQueue.Count;
	}

	public void DisplayNext(){
		DisplayNext(true);
	}

	public void DisplayNext(bool force){
		if (displayingAlert && !force){
			return;
		}

		if (alertQueue.Count == 0){
			Visible = displayingAlert = false;
			return;
		}

		Visible = true;

		Alert alert = alertQueue[0];
		titleLabel.Text = alert.titleText;
		subtextLabel.Text = alert.subText;
		confirmButton.Text = alert.buttonText;

		subtextSeperator.Visible = alert.subText.Length > 0;

		alertQueue.RemoveAt(0);
		displayingAlert = true;

		UpdateExtraDisplay();
	}

	public void NewAlert(string title){
		NewAlert(new Alert(){
			titleText = title,
			subText = "",
			buttonText = defaultButtonText
		});
	}

	public void NewAlert(string title, string subtext){
		NewAlert(new Alert(){
			titleText = title,
			subText = subtext,
			buttonText = defaultButtonText
		});
	}

	public void NewAlert(string title, string subtext, string button){
		NewAlert(new Alert(){
			titleText = title,
			subText = subtext,
			buttonText = button
		});
	}

	public void NewAlert(Alert alert){
		alertQueue.Add(alert);
		UpdateExtraDisplay();
		DisplayNext(false);
	}

	public class Alert{
		public string titleText;
		public string subText;
		public string buttonText;
	}
}
