using System.Collections.Generic;
using Godot;
using Godot.NativeInterop;

public partial class MessageCreator : MarginContainer
{
	[Signal] public delegate void OnMessageSubmitEventHandler(string message, string[] embedPaths, string replyingTo);

	[Export] public TextEdit messageInput;
	[Export] public FileDialog embedDialog;

	[Export] public Control embedContainer;

	[Export] public PackedScene embedScene;

	[Export] public Control replyModel;
	[Export] public Label replyPreview;

	public string replyingToMessage;

	private Dictionary<string, Node> embeds = new Dictionary<string, Node>();
	private PeerService peerService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		peerService = GetNode<PeerService>("/root/Main/Bugcord/PeerService");
		GetWindow().FilesDropped += EmbedFile;
		ClearReply(); // Just to get rid of the reply preview
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

			if (sendingText.Length == 0 && embeds.Count == 0){ // If message would be empty
				return;
			}

			// Convert embed dictionary to an array
			List<string> embedPaths = new List<string>();
			foreach (string path in embeds.Keys){
				embedPaths.Add(path);
			}

			EmitSignal(SignalName.OnMessageSubmit, sendingText, embedPaths.ToArray(), replyingToMessage);
			messageInput.Clear();
			ClearReply();

			foreach (Node embedNode in embeds.Values){
				embedNode.QueueFree();
			}
			embeds.Clear();
		}
	}

	public void SetReply(DatabaseService.Message message){
		replyingToMessage = message.id;
		replyModel.Visible = true;

		string replyPreviewText = peerService.peers[message.senderId].username;
		replyPreviewText += ": " + message.content;
		replyPreview.Text = replyPreviewText;
	}

	public void ClearReply(){
		replyingToMessage = null;
		replyModel.Visible = false;
	}

	public void EmbedFile(string[] dirs){
		GD.Print(dirs.Length);
		foreach (string dir in dirs){
			// read some metadata
			string filename = System.IO.Path.GetFileName(dir);
			FileAccess dirFile = FileAccess.Open(dir, FileAccess.ModeFlags.Read);
			string fileSize = BugstringUtils.BytesToSizeString(dirFile.GetLength());
			dirFile.Close();

			embed_input_display newEmbed = embedScene.Instantiate<embed_input_display>();
			newEmbed.Initiate(filename, fileSize, dir);
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
