using Godot;
using System;
using System.Collections.Generic;

public partial class EventChainService : Node
{
	public const string chainStorePath = "user://serve/";
	public const ushort eventChainVersion = 0;
	public const ushort eventVersion = 0;

	public Dictionary<string, EventPacket> currentChainEnd = new();

	// Chain id, previous chain
	private Dictionary<string, string> activeChainRequest = new();

	private Bugcord bugcord;
	private RequestService requestService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		bugcord = GetParent<Bugcord>();
		requestService = GetParent().GetNode<RequestService>("RequestService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void ChainRequestSuccess(string fileId){
		if (!activeChainRequest.ContainsKey(fileId))
			return;

		GD.Print("Chain retrieved from peers");

		activeChainRequest.Remove(fileId);

		LoadEventChainFile(fileId, true, true);
	}

	public void ChainRequestFailed(string fileId){
		if (!activeChainRequest.ContainsKey(fileId))
			return;

		GD.Print("Chain request failed");

		if (!FileAccess.FileExists(chainStorePath + fileId + ".chain"))
			InitEventChain(fileId, activeChainRequest[fileId]);

		activeChainRequest.Remove(fileId);
		bugcord.FinishRetrivingChain();
	}

	public void LoadEventChain(string keyStart){
		LoadEventChain(keyStart, keyStart);
	}

	public void LoadEventChain(string keyStart, string prevKey){
		GD.Print("Loading chain " + keyStart);

		// Get the chain from the network
		activeChainRequest.Add(keyStart, prevKey);

		requestService.OnRequestSuccess += ChainRequestSuccess;
		requestService.OnRequestFailed += ChainRequestFailed;

		requestService.Request(keyStart, RequestService.FileExtension.EventChain, RequestService.VerifyMethod.Consensus, 1f);
	}

	public void SaveEvent(byte[] data, string keyChain){
		double timestamp = Time.GetUnixTimeFromSystem();

		byte[] hash = new byte[0];
		if (currentChainEnd.ContainsKey(keyChain)){
			List<byte> hashConcat = new List<byte>();
			EventPacket prevEvent = currentChainEnd[keyChain];

			hashConcat.AddRange(prevEvent.data);
			hashConcat.AddRange(BitConverter.GetBytes(prevEvent.timestamp));

			hash = KeyService.GetSHA256Hash(hashConcat.ToArray());
		}

		EventPacket eventPacket = new EventPacket(){
			data = data,
			timestamp = timestamp,
		};

		currentChainEnd[keyChain] = eventPacket;

		AppendEventToFile(eventPacket, keyChain);
	}

	public void LoadEventChainFile(string keyChain, bool tryGetNextChain, bool processEvents){
		FileAccess eventFile = FileAccess.Open(chainStorePath + "/" + keyChain + ".chain", FileAccess.ModeFlags.Read);

		byte[] eventChainRaw = eventFile.GetBuffer((long)eventFile.GetLength());
		byte[][] dataspans = Bugcord.ReadDataSpans(eventChainRaw, 3);

		ushort chainVersion = BitConverter.ToUInt16(eventChainRaw, 0);
		if (chainVersion > eventChainVersion){
			GD.PrintErr("Chain version not supported. Version: " + chainVersion + " Supported: " + eventChainVersion);
			return;
		}

		string prevChain = dataspans[0].GetStringFromUtf8();
		string nextChain = null;

		bool isFinishedChain = eventChainRaw[2] == 0x01;
		if (isFinishedChain)
			nextChain = dataspans[dataspans.Length - 1].GetStringFromUtf8();

		if (tryGetNextChain && nextChain != null){
			LoadEventChain(nextChain);
		}

		if (processEvents){
			int endOffset = 0;
			if (isFinishedChain)
				endOffset = 1;
			for (int i = 1; i < dataspans.Length - endOffset; i++){
				byte[][] eventDataspans = Bugcord.ReadDataSpans(dataspans[i], 10);

				ushort eventVersion = BitConverter.ToUInt16(dataspans[i], 0);
				double eventTimestamp = BitConverter.ToDouble(dataspans[i], 2);

				if (eventVersion > eventChainVersion)
					continue;

				EventPacket eventPacket = new EventPacket
				{
					data = eventDataspans[0],
					timestamp = eventTimestamp,
				};
				
				bugcord.ProcessIncomingPacket((PacketService.Packet)eventPacket, true);
			}
		}
	}

	public void AppendEventToFile(EventPacket packet, string keyChain){
		GD.Print("Appending: " + chainStorePath + keyChain + ".chain");

		if (!FileAccess.FileExists(chainStorePath + keyChain + ".chain")){
			FileAccess newEventFile = FileAccess.Open(chainStorePath + keyChain + ".chain", FileAccess.ModeFlags.Write);
		}

		List<byte> eventSection = new List<byte>();

		eventSection.AddRange(BitConverter.GetBytes(eventVersion));
		eventSection.AddRange(BitConverter.GetBytes(packet.timestamp));
		eventSection.AddRange(Bugcord.MakeDataSpan(packet.data));

		FileAccess eventFile = FileAccess.Open(chainStorePath + keyChain + ".chain", FileAccess.ModeFlags.ReadWrite);

		eventFile.SeekEnd();
		eventFile.StoreBuffer(Bugcord.MakeDataSpan(eventSection.ToArray()));
		eventFile.Close();
	}

	public void InitEventChain(string chainId, string previousChainId){
		GD.Print("Initializing chain: " + chainId + " Previous chain: " + previousChainId);

		List<byte> eventSection = new List<byte>();

		eventSection.AddRange(BitConverter.GetBytes(eventChainVersion));
		eventSection.Add(0);
		eventSection.AddRange(Bugcord.MakeDataSpan(previousChainId.ToUtf8Buffer()));

		FileAccess newEventFile = FileAccess.Open(chainStorePath + chainId + ".chain", FileAccess.ModeFlags.Write);
		newEventFile.StoreBuffer(eventSection.ToArray());
		newEventFile.Close();
	}

	public class EventPacket{
		public byte[] data;
		public double timestamp;

		public static explicit operator PacketService.Packet(EventPacket eventPacket){
			PacketService.Packet packet = new PacketService.Packet(){
				timestamp = eventPacket.timestamp,
				data = eventPacket.data,
			};

			return packet;
		}
	}
}
