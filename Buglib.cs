using Godot;
using System;
using System.Reflection;
using System.Text;

public partial class Buglib : Node
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	// String conversion from https://stackoverflow.com/a/17001289
	public static string BytesToHex(byte[] bytes){
		StringBuilder builder = new StringBuilder();

		foreach (byte b in bytes){
			builder.Append(b.ToString("x2"));
		}

		return builder.ToString();
	}
}
