using Godot;
using System;

public partial class RequestDebug : MarginContainer
{
	[Export] public Label idLabel;
	[Export] public Label timeLeftLabel;
	[Export] public Label partsLabel;

	private int partsRecieved;
	private float timeLeft;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public override void _Process(double delta)
	{
		timeLeft -= (float)delta;
		timeLeftLabel.Text = timeLeft.ToString();
	}

	public void Initiate(string id, float timeout){
		idLabel.Text = id;
		timeLeft = timeout;
		partsLabel.Text = partsRecieved.ToString();
	}

	public void IncrementParts(){
		partsRecieved++;
		partsLabel.Text = partsRecieved.ToString();
	}
}
