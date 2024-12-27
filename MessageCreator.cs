using System.Collections.Generic;
using Godot;
using Godot.NativeInterop;

public partial class MessageCreator : MarginContainer
{
	[Signal] public delegate void OnMessageSubmitEventHandler();

	[Export] public TextEdit messageInput;
	[Export] public FileDialog embedDialog;

	[Export] public Control embedContainer;

	[Export] public PackedScene embedScene;

	[Export] public Control replyModel;
	[Export] public Label replyPreview;

	public string replyingToMessage;

	private List<string> embedPaths = new List<string>();

	private Dictionary<string, Node> embedUiElements = new Dictionary<string, Node>();
	private Dictionary<string, Image> embedImages = new Dictionary<string, Image>();
	private PeerService peerService;
	private FileService fileService;

	private Bugcord bugcord;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		bugcord = GetNode<Bugcord>("/root/Main/Bugcord");
		peerService = GetNode<PeerService>("/root/Main/Bugcord/PeerService");
		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
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

		if (Input.IsActionJustPressed("Paste") && DisplayServer.ClipboardHasImage()){
			EmbedFile();
		}

		if (Input.IsActionJustPressed("Enter")){
			messageInput.Undo(); // remove newline that was just added
			string sendingText = messageInput.Text;

			if (sendingText.Length == 0 && embedUiElements.Count == 0){ // If message would be empty
				return;
			}

			// Save the images stored from the clipboard to the cache so we dont have to rewrite how embeds work
			foreach (KeyValuePair<string, Image> image in embedImages){
				string cachePath = fileService.WriteToCache(image.Value.SavePngToBuffer(), image.Key+".png", image.Key, false);
				if (cachePath != null){
					embedPaths.Add(cachePath);
				}
			}

			// Convert embed dictionary to an array
			// List<string> embedPaths = new List<string>();
			// foreach (string path in embedUiElements.Keys){
			// 	// If this embed is a clipboard embed
			// 	string[] pathParsed = path.Split('_', System.StringSplitOptions.RemoveEmptyEntries);
			// 	embedPaths.Add(path);
			// }

            bugcord.PostMessage(sendingText, embedPaths.ToArray(), replyingToMessage);
			EmitSignal(SignalName.OnMessageSubmit);

			// Reset the message window
			messageInput.Clear();
			ClearReply();

			foreach (Node embedNode in embedUiElements.Values){
				embedNode.QueueFree();
			}
			embedPaths.Clear();
			embedUiElements.Clear();
		}
	}

	public void SetReply(DatabaseService.Message message){
		replyingToMessage = message.id;
		replyModel.Visible = true;

		string replyPreviewText = peerService.GetPeer(message.senderId).username;
		replyPreviewText += ": " + message.content;
		replyPreview.Text = replyPreviewText;
	}

	public void ClearReply(){
		replyingToMessage = null;
		replyModel.Visible = false;
	}

	/// <summary>
	/// Embeds the image in the clipboard
	/// </summary>
	public void EmbedFile(){
		if (!DisplayServer.ClipboardHasImage())
			return;

		GD.Print("MessgeCreator: Pasting clipboard image");

		string tempImageId = Buglib.GetRandomHexString(16);

		Image image = DisplayServer.ClipboardGetImage();

		embedImages.Add(tempImageId, image);

		embed_input_display newEmbed = embedScene.Instantiate<embed_input_display>();
		newEmbed.Initiate("Pasted Image", Buglib.ShortenDataSize(image.GetData().Length), tempImageId);
		newEmbed.OnRemoveEmbed += RemoveEmbed;

		embedContainer.AddChild(newEmbed);

		embedUiElements.Add(tempImageId, newEmbed);
	}

	/// <summary>
	/// Embeds the file at a path
	/// </summary>
	public void EmbedFile(string[] dirs){
		foreach (string dir in dirs){
			// read some metadata
			string filename = System.IO.Path.GetFileName(dir);
			FileAccess dirFile = FileAccess.Open(dir, FileAccess.ModeFlags.Read);
			string fileSize = Buglib.ShortenDataSize((long)dirFile.GetLength());
			dirFile.Close();

			embed_input_display newEmbed = embedScene.Instantiate<embed_input_display>();
			newEmbed.Initiate(filename, fileSize, dir);
			newEmbed.OnRemoveEmbed += RemoveEmbed;

			embedContainer.AddChild(newEmbed);

			embedUiElements.Add(dir, newEmbed);

			embedPaths.Add(dir);
		}
	}

	public void RemoveEmbed(string embedDir){
		embedUiElements[embedDir].QueueFree();
		embedUiElements.Remove(embedDir);
	}

	public void OnEmbedButtonPressed(){
		embedDialog.Show();
	}
}
