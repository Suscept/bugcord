using Godot;
using System;

public partial class MessageCreator : MarginContainer
{
	[Signal] public delegate void OnMessageSubmitEventHandler(string message);

	[Export] public TextEdit messageInput;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (Input.IsActionJustPressed("ShftEnter")){
			messageInput.InsertTextAtCaret("\n");
			return;
		}

		if (Input.IsActionJustPressed("Enter")){
			messageInput.Undo(); // remove newline that was just added
			string sendingText = messageInput.Text;

			if (sendingText.Length > 0){ // If message is not empty
				EmitSignal(SignalName.OnMessageSubmit, sendingText);
				messageInput.Clear();
			}
		}
	}
}
