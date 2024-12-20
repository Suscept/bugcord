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

	[Export] public float minRequestTime = 2;
	[Export] public float maxRequestTime = 15;

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
			pendingRequest.timeWaited += (float)delta;
			if (pendingRequest.timeWaited > pendingRequest.maxWaitTime){
				// Request timeout
				GD.Print("Request " + pendingRequest.id + " timed out");
				EmitSignal(SignalName.OnRequestTimeout, pendingRequest.id);
				EmitSignal(SignalName.OnRequestFailed, pendingRequest.id);
				activeRequests.Remove(pendingRequest.id);
			}else if  (pendingRequest.timeWaited > pendingRequest.minWaitTime && pendingRequest.anyResponceFinished){
				WrapUpRequest(pendingRequest);
			}
		}
	}

	public void Request(string id, FileExtension extension, VerifyMethod verifyMethod){
		Request(id, extension, verifyMethod, maxRequestTime, null);
	}

	public void Request(string id, FileExtension extension, VerifyMethod verifyMethod, Action<string> subscription){
		Request(id, extension, verifyMethod, maxRequestTime, subscription);
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
			maxWaitTime = timeout,
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
				maxWaitTime = maxRequestTime * 2,
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
			
			RequestResponce responce = new RequestResponce{
				respondingPeer = senderId,
				segments = bytesList,
				expectedSegments = filePartMax
			};

			request.responces.Add(senderId, responce);
		}
	
		request.responces[senderId].segments[filePart] = file;

		// Find how many parts have been recieved so far
		int filePartsRecieved = 0;
		int filePartsTotal = request.responces[senderId].segments.Count;
		for (int i = 0; i < filePartsTotal; i++){
			if (request.responces[senderId].segments[i] != null){
				filePartsRecieved++;
			}
		}

		EmitSignal(SignalName.OnRequestProgress, fileId);

		if (filePartsRecieved < filePartsTotal){
			return;
		}

		request.anyResponceFinished = true;

		// If this request can recieve multiple responces
		bool multipleResponces = request.verifyMethod == VerifyMethod.NewestSignature || request.verifyMethod == VerifyMethod.Consensus;

		if (multipleResponces && request.timeWaited < request.minWaitTime){
			return; // Some more time is needed to collect responces
		}

		WrapUpRequest(request);
	}

	/// <summary>
	/// Ends a request by success or failure
	/// </summary>
	/// <param name="request"></param>
	public void WrapUpRequest(PendingRequest request){
		GD.Print("RequestService: Wrapping up request: " + request.id);

		Dictionary<string, byte[]> fullFiles = new Dictionary<string, byte[]>();

		foreach(KeyValuePair<string, RequestResponce> responceEntry in request.responces){
			string senderId = responceEntry.Key;
			RequestResponce responce = responceEntry.Value;

			// Find how many parts have been recieved so far
			int filePartsRecieved = 0;
			int filePartsTotal = request.responces[senderId].segments.Count;
			for (int i = 0; i < filePartsTotal; i++){
				if (request.responces[senderId].segments[i] != null){
					filePartsRecieved++;
				}
			}

			if (filePartsRecieved < responce.expectedSegments){ // This responce is unfinished
				continue;
			}

			// Concatinate the responces
			List<byte> fullFile = new();
			for (int i = 0; i < request.responces[senderId].segments.Count; i++){
				fullFile.AddRange(request.responces[senderId].segments[i]);
			}
			fullFiles.Add(senderId, fullFile.ToArray());
		}

		string bestResponceSender = null;
		byte[] bestResponceData = null;
		double bestResponceScore = 0;

		// Pick a responce to use
		GD.Print("- Validating responces. Method: " + request.verifyMethod.ToString());
		foreach (KeyValuePair<string, byte[]> file in fullFiles){
			switch (request.verifyMethod)
			{
				case VerifyMethod.NewestSignature:
					if (request.extension != FileExtension.PeerData){
						bestResponceData = file.Value;
						bestResponceSender = file.Key;
					}

					// Currently only peer packages use this so a more generic solution should be made in the future
					ushort version = BitConverter.ToUInt16(file.Value, 0);
					if (PeerService.peerPackageVersion > version){ // Version not supported
						GD.Print("- Failed. Incorrect version: " + version + " Supported: " + PeerService.peerPackageVersion);
						continue;
					}

					byte[] signature = Buglib.ReadLength(file.Value, 2, 256);
					byte[] signedData = Buglib.ReadLengthInfinitely(file.Value, 258);
					double score = BitConverter.ToDouble(file.Value, 258); // timestamp

					byte[][] dataspans = Buglib.ReadDataSpans(file.Value, 266);
					byte[] publicKey = dataspans[1];

					// Since this is a peer file, it may be the first time this peer has sent its file so this client doesnt already have it's public key
					// So we use the public key from this file package. Making sure to also verify everything properly
					if (KeyService.GetSHA256HashString(publicKey) != file.Key){
						GD.Print("- Failed. Key hash mismatch");
						continue;
					}

					if (!KeyService.VerifySignature(signedData, signature, publicKey)){
						GD.Print("- Failed. Bad signature");
						continue;
					}

					if (score > bestResponceScore){
						bestResponceData = file.Value;
						bestResponceSender = file.Key;
						bestResponceScore = score;
					}

					continue;
				case VerifyMethod.HashCheck:
					string computedFileId = KeyService.GetSHA256HashString(file.Value);
					if (request.id != computedFileId){
						GD.PrintErr("File hash mismatch! Packet: " + request.id + " Actual: " + computedFileId);
						EmitSignal(SignalName.OnRequestFailed, request.id);
						continue;
					}

					bestResponceData = file.Value;
					bestResponceSender = file.Key;
					break;
				default:
					bestResponceData = file.Value;
					bestResponceSender = file.Key;
					break;
			}
		}

		activeRequests.Remove(request.id);

		if (bestResponceData == null){
			GD.Print("- Request wrap up failed");
			return;
		}

		GD.Print("- Best responce picked. Sender: " + bestResponceSender);

		// Save the file

		fileService.WriteToServableAbsolute(bestResponceData, request.id + GetFileExtensionString(request.extension));

		if (request.cacheDesired && request.extension == FileExtension.MediaFile){
			bool success = fileService.UnpackageFile(bestResponceData, out byte[] fileData, out string filename);
			if (success)
				fileService.WriteToCache(fileData, filename, request.id);
		}
		
		EmitSignal(SignalName.OnRequestSuccess, request.id);
		TriggerSubscriptions(request.id);

		GD.Print("- Request wrapped up successfully!");
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

		public bool anyResponceFinished;

		public float timeWaited;
		public float maxWaitTime;
		public float minWaitTime;

		// Responder id, responce segments
		// public Dictionary<string, List<byte[]>> responces = new Dictionary<string, List<byte[]>>();
		public Dictionary<string, RequestResponce> responces = new Dictionary<string, RequestResponce>();
	}

	public class RequestResponce{
		public string respondingPeer;

		public List<byte[]> segments = new List<byte[]>();

		public int expectedSegments;
	}
}
