using Godot;
using System;
using System.Collections.Generic;

public partial class RequestService : Node
{
	[Signal] public delegate void OnRequestCreatedEventHandler(string requestId);
	[Signal] public delegate void OnRequestSuccessEventHandler(string requestId);
	[Signal] public delegate void OnRequestTimeoutEventHandler(string requestId);
	[Signal] public delegate void OnRequestFailedEventHandler(string requestId);

	[Signal] public delegate void OnRequestProgressEventHandler(string requestId);

	[Export] public float requestTimeoutTime = 15;

	public Dictionary<string, List<Action<string>>> subscriptions = new();

	private FileService fileService;
	private PacketService packetService;
	private Bugcord bugcord;

	public enum VerifyMethod
	{
		HashCheck,			// If the file's id is the same as it's hash
		Consensus,			// Multiple reputable peers return the same thing
		None,				// Nothing. Use the first result
		NewestSignature,	// Whichever result is the newest and signed by it's owner
	}

	public enum FileExtension{
		MediaFile = 0,
		EventChain = 1,
		SpaceData = 2,
		PeerData = 3,
	}

	// File id, request class
	public Dictionary<string, PendingRequest> activeRequests = new Dictionary<string, PendingRequest>();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		fileService = GetParent().GetNode<FileService>("FileService");
		packetService = GetParent().GetNode<PacketService>("PacketService");
		bugcord = GetParent<Bugcord>();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		foreach (PendingRequest pendingRequest in activeRequests.Values){
			pendingRequest.timeLeft -= (float)delta;
			if (pendingRequest.timeLeft <= 0){
				// Request timeout
				GD.Print("Request " + pendingRequest.id + " timed out");
				EmitSignal(SignalName.OnRequestTimeout, pendingRequest.id);
				EmitSignal(SignalName.OnRequestFailed, pendingRequest.id);
				activeRequests.Remove(pendingRequest.id);
			}
		}
	}

	public void Request(string id, FileExtension extension, VerifyMethod verifyMethod){
		Request(id, extension, verifyMethod, requestTimeoutTime, null);
	}

	public void Request(string id, FileExtension extension, VerifyMethod verifyMethod, Action<string> subscription){
		Request(id, extension, verifyMethod, requestTimeoutTime, subscription);
	}

	public void Request(string id, FileExtension extension, VerifyMethod verifyMethod, float timeout){
		Request(id, extension, verifyMethod, timeout, null);
	}

	public void Request(string id, FileExtension extension, VerifyMethod verifyMethod, float timeout, Action<string> subscription){
		GD.Print("Requesting file: " + id);
		if (activeRequests.ContainsKey(id)){
			GD.Print("- Request already exists");
			return;
		}

		PendingRequest newRequest = new PendingRequest(){
			id = id,
			extension = extension,
			verifyMethod = verifyMethod,
			timeLeft = timeout,
			cacheDesired = true,
		};

		activeRequests.Add(id, newRequest);

		packetService.SendPacket(bugcord.BuildFileRequest(id, (byte)extension));

		EmitSignal(SignalName.OnRequestCreated, id);

		if (subscription != null){
			if (!subscriptions.ContainsKey(id)){
				GD.Print("- Creating new subscription list");
				subscriptions.Add(id, new List<Action<string>>());
			}
			subscriptions[id].Add(subscription);
			GD.Print("- Subscription added");
		}

		GD.Print("- Request created");
	}

	public void ProcessRequestResponse(byte[] packet){
		GD.Print("recieved file packet");

		byte[][] dataSpans = Buglib.ReadDataSpans(packet, 6);

		byte subtype = packet[1];

		ushort filePart = BitConverter.ToUInt16(packet, 2);
		ushort filePartMax = BitConverter.ToUInt16(packet, 4);

		string fileGuid = dataSpans[0].GetStringFromUtf8();
		string senderGuid = dataSpans[1].GetStringFromUtf8();
		byte[] fileData = dataSpans[2];

		if (fileService.IsFileInCache(fileGuid)){ // File already in cache
			return;
		}

		UpdateDataBuffer(filePart, filePartMax, fileGuid, senderGuid, fileData, (FileExtension)subtype);
	}

	public void UpdateDataBuffer(ushort filePart, ushort filePartMax, string fileId, string senderId, byte[] file, FileExtension extension){
		if (!activeRequests.ContainsKey(fileId)){
			PendingRequest newRequest = new PendingRequest(){
				id = fileId,
				extension = extension,
				verifyMethod = VerifyMethod.None,
				timeLeft = requestTimeoutTime * 2,
				cacheDesired = false,
			};

			activeRequests.Add(fileId, newRequest);
		}
		PendingRequest request = activeRequests[fileId];

		// If this responder has not sent parts before a list must be created
		if (!request.responces.ContainsKey(senderId)){
			// Make a list of empty byte arrays for each expected part of the file
			List<byte[]> bytesList = new List<byte[]>();
			byte[][] bytes = new byte[filePartMax][];
			bytesList.AddRange(bytes);

			request.responces.Add(senderId, bytesList); 
		}
	
		request.responces[senderId][filePart] = file;

		// Find how many parts have been recieved so far
		int filePartsRecieved = 0;
		int filePartsTotal = request.responces[senderId].Count;
		for (int i = 0; i < filePartsTotal; i++){
			if (request.responces[senderId][i] != null){
				filePartsRecieved++;
			}
		}

		EmitSignal(SignalName.OnRequestProgress, fileId);

		if (filePartsRecieved < filePartsTotal){
			return;
		}

		// No parts are missing so concatinate everything, verify and save
		List<byte> fullFile = new();
		for (int i = 0; i < request.responces[senderId].Count; i++){
			fullFile.AddRange(request.responces[senderId][i]);
		}
		request.responces.Remove(senderId);
		activeRequests.Remove(request.id);

		switch (request.verifyMethod)
		{
			case VerifyMethod.Consensus:
				break;
			case VerifyMethod.HashCheck:
				string computedFileId = KeyService.GetSHA256HashString(fullFile.ToArray());
				if (fileId != computedFileId){
					GD.PrintErr("File hash mismatch! Packet: " + fileId + " Actual: " + computedFileId);
					EmitSignal(SignalName.OnRequestFailed, request.id);
					return;
				}
				break;
			case VerifyMethod.None:
				break;
			default:
				break;
		}

		fileService.WriteToServableAbsolute(fullFile.ToArray(), fileId + GetFileExtensionString(request.extension));
		if (request.cacheDesired && request.extension == FileExtension.MediaFile){
			bool success = fileService.UnpackageFile(fullFile.ToArray(), out byte[] fileData, out string filename);
			if (success)
				fileService.WriteToCache(fileData, filename, fileId);
		}
		
		EmitSignal(SignalName.OnRequestSuccess, request.id);
		TriggerSubscriptions(fileId);
	}

	public void TriggerSubscriptions(string id){
		if (!subscriptions.ContainsKey(id))
			return;

		foreach (Action<string> subscription in subscriptions[id]){
			subscription.Invoke(id);
		}
		subscriptions.Remove(id);
	}

	public static string GetFileExtensionString(FileExtension fileExtension){
		switch (fileExtension)
		{
			case FileExtension.MediaFile:
				return ".file";
			case FileExtension.EventChain:
				return ".chain";
			case FileExtension.SpaceData:
				return ".space";
			case FileExtension.PeerData:
				return ".peer";
			default:
				return "";
		}
	}

	public class PendingRequest{
		public string id;
		public FileExtension extension;
		public VerifyMethod verifyMethod;

		public bool cacheDesired;

		public float timeLeft;

		// Responder id, responce segments
		public Dictionary<string, List<byte[]>> responces = new Dictionary<string, List<byte[]>>();
	}
}
