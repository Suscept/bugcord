using Godot;
using Godot.Collections;
using Godot.NativeInterop;

public partial class MessageCreator : MarginContainer
{
	[Signal] public delegate void OnMessageSubmitEventHandler(string message);
	[Signal] public delegate void OnEmbedSubmitEventHandler(string filePath);

	[Export] public TextEdit messageInput;
	[Export] public FileDialog embedDialog;

	[Export] public Control embedContainer;

	[Export] public PackedScene embedScene;

	private Dictionary<string, Node> embeds = new Dictionary<string, Node>();

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

			if (embeds.Count > 0){ // If embeds are present
				foreach (string path in embeds.Keys){
					EmitSignal(SignalName.OnEmbedSubmit, path);
				}

				foreach (Node embedNode in embeds.Values){
					embedNode.QueueFree();
				}
				embeds.Clear();
			}
		}
	}

	public void EmbedFile(string[] dirs){
		GD.Print(dirs.Length);
		foreach (string dir in dirs){
			embed_input_display newEmbed = embedScene.Instantiate<embed_input_display>();
			newEmbed.Initiate("newfile.ree", "1000 TB", dir);
			newEmbed.OnRemoveEmbed += RemoveEmbed;

			embedContainer.AddChild(newEmbed);

			embeds.Add(dir, newEmbed);
		}
	}

	public void RemoveEmbed(string embedDir){
		embeds[embedDir].QueueFree();
		embeds.Remove(embedDir);
	}

	public void OnEmbedButtonPressed(){
		embedDialog.Show();
	}
}
