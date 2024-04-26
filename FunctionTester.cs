using Godot;
using System;

public partial class FunctionTester : Panel
{
	[Export] public LineEdit input;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void OnInput(){
		foreach (byte[] b in Bugcord.ReadDataSpans(Bugcord.FromBase64(input.Text), 0)){
			foreach (byte bt in b){
				GD.Print(bt);
			}
		}
		// GD.Print(Bugcord.ReadDataSpan(Bugcord.FromBase64(input.Text), 0));
	}
}
