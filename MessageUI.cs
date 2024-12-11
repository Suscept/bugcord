using Godot;
using Godot.Collections;
using System;

public partial class MessageUI : MarginContainer
{
	[Export] public float maxHeightFromImage;

	[Export] public TextureRect imageContent;
	[Export] public TextureRect profilePicture;
	[Export] public RichTextLabel textContent;
	[Export] public Label usernameLabel;
	[Export] public Label mediaLoadingProgressLabel;
	[Export] public Label timestampLabel;
	[Export] public MenuButton extraOptionsDropdown;
	[Export] public Button jumpToReplyButton;

	public string waitingForEmbedGuid;
	private int embedPartsRecieved;

	private PopupMenu popupMenu;
	private DatabaseService.Message myMessage;

	private FileService fileService;
	private RequestService requestService;
	private PeerService peerService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		popupMenu = extraOptionsDropdown.GetPopup();
		popupMenu.IdPressed += OnDropdownOptionPressed;
	}

	public void OnDropdownOptionPressed(long optionId){
		switch (optionId)
		{
			case 0: // Reply
				MessageCreator messageCreator = GetNode<MessageCreator>("/root/Main/HBoxContainer/ChatMain/VBoxContainer/MessageCreator");
				messageCreator.SetReply(myMessage);
				break;
			case 1: // Copy Text
				DisplayServer.ClipboardSet(textContent.Text);
				break;
			case 2: // Delete
				GetNode<PopupAlert>("/root/Main/Popups/GenericPopup").NewAlert("Not implemented lol");
				break;
			case 3: // Download embed
				GetNode<PopupAlert>("/root/Main/Popups/GenericPopup").NewAlert("Not implemented lol");
				break;
			default:
				break;
		}
	}

	public void Initiate(DatabaseService.Message message, string replyPreview, FileService setFileService, PeerService setPeerService){
		fileService = setFileService;
		peerService = setPeerService;

		usernameLabel.Text = peerService.GetPeer(message.senderId).username;
		timestampLabel.Text = GetTimestampString(message.unixTimestamp);
		myMessage = message;

		bool profileAvailable = peerService.GetProfilePicture(message.senderId, out ImageTexture profileImage);
		if (profileAvailable){
			SetProfilePicture(profileImage, message.senderId);
		}else{
			peerService.OnProfileImageAvailable += SetProfilePicture;
		}

		jumpToReplyButton.Visible = replyPreview != null;
		if (replyPreview != null){
			jumpToReplyButton.Text = "┌ " + replyPreview;
		}

		if (message.content != null && message.content.Length > 0){
			SetupMessageContent(message.content);
		}else{
			textContent.Visible = false;
		}

		if (message.embedId != null && message.embedId.Length > 0){
			waitingForEmbedGuid = message.embedId;

			mediaLoadingProgressLabel.Visible = true;
			mediaLoadingProgressLabel.Text = "Loading...";
		}else{
			imageContent.Visible = false;
			mediaLoadingProgressLabel.Visible = false;
		}
	}

	public void SetupMessageContent(string text){
		string sanitizedText = text.Replace("[", "[lb]"); // Removes the ability for users to add their own BBCode tags to their messages

		string[] lines = sanitizedText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		string processedLines = "";
		for (int i = 0; i < lines.Length; i++){
			if (lines[i][0] == '>'){ // Greentext
				lines[i] = lines[i].Insert(0, "[color=green]") + "[/color]";
			}

			if (i != lines.Length - 1){
				lines[i] += '\n';
			}
			processedLines += lines[i];
		}

		textContent.Text = processedLines;
	}

	public void CacheUpdated(string guid){
		if (guid != waitingForEmbedGuid)
			return;

		fileService = GetNode<FileService>("/root/Main/Bugcord/FileService");
		requestService = GetNode<RequestService>("/root/Main/Bugcord/RequestService");
		string cachePath = fileService.cacheIndex[guid].path;
		SetupMediaUi(cachePath);

		popupMenu.AddItem("Download", 3);

		mediaLoadingProgressLabel.Visible = false;
		fileService.OnCacheChanged -= CacheUpdated;
		requestService.OnRequestProgress -= FileBufferUpdated;
	}

	public void FileBufferUpdated(string guid){
		if (guid != waitingForEmbedGuid)
			return;

		embedPartsRecieved++;
		mediaLoadingProgressLabel.Text = "Loading... " + embedPartsRecieved;
	}

	public void SetProfilePicture(ImageTexture pfp, string peerId){
		if (peerId != myMessage.senderId || pfp == null)
			return;

		peerService.OnProfileImageAvailable -= SetProfilePicture;
		profilePicture.Texture = pfp;
	}

	public void OnImageClicked(InputEvent inputEvent){
		if (inputEvent is InputEventMouseButton mouseEvent){
			if (mouseEvent.ButtonMask == MouseButtonMask.Left){
				GetNode<EmbedPreviewer>("/root/Main/Popups/EmbedPreview").PreviewEmbed(waitingForEmbedGuid);
			}
		}
	}

	public void OnUserDisplayInput(InputEvent @event){
		if (@event is InputEventMouseButton mouseEvent){
			if (mouseEvent.ButtonIndex.HasFlag(MouseButton.Left) && mouseEvent.Pressed){
				GetNode<ProfileViewer>("/root/Main/Popups/ProfileViewer").ViewPeerProfile(peerService.GetPeer(myMessage.senderId));
			}
		}
	}

	private string GetTimestampString(double timestamp){
		string dateString = Time.GetDateStringFromUnixTime((long)timestamp);
		if (Time.GetDateStringFromSystem() == dateString){
			dateString = "Today at";
		}
		int timezoneOffset = (int)Time.GetTimeZoneFromSystem()["bias"] * 60;

		string timeString = Time.GetTimeStringFromUnixTime((long)timestamp + timezoneOffset);
		string[] timeSplit = timeString.Substring(0, timeString.Length - 3).Split(':'); // Remove seconds

		// Convert timestamp to 12 hour time
		int hour = timeSplit[0].ToInt();
		string amOrPm = " AM";
		if (hour >= 12){
			amOrPm = " PM";
		}

		if (hour == 0){
			hour += 12;
		}else if (hour > 12){
			hour -= 12;
		}

		timeSplit[0] = hour.ToString();
		timeString = timeSplit.Join(":");

		dateString = dateString.Replace('-', '/');

		return dateString + " " + timeString + amOrPm;
	}

	private void SetupMediaUi(string cachePath){
		Image loadedImage = Image.LoadFromFile(cachePath);
		ImageTexture imageTexture = ImageTexture.CreateFromImage(loadedImage);

		imageContent.Texture = imageTexture;

		imageContent.CustomMinimumSize = new Vector2(0, Mathf.Min(maxHeightFromImage, loadedImage.GetSize().Y));
	}
}
