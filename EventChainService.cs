using Godot;
using System;
using System.Collections.Generic;

public partial class EventChainService : Node
{
	public const string chainStorePath = "user://serve/";

	public Dictionary<string, EventPacket> currentChainEnd = new();

	private Bugcord bugcord;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		bugcord = GetParent<Bugcord>();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void SaveEvent(byte[] data, string keyChain){
		double timestamp = Time.GetUnixTimeFromSystem();

		byte[] hash = new byte[0];
		if (currentChainEnd.ContainsKey(keyChain)){
			List<byte> hashConcat = new List<byte>();
			EventPacket prevEvent = currentChainEnd[keyChain];

			hashConcat.AddRange(prevEvent.data);
			hashConcat.AddRange(BitConverter.GetBytes(prevEvent.timestamp));
			hashConcat.AddRange(prevEvent.prevEventHash);

			hash = KeyService.GetSHA256Hash(hashConcat.ToArray());
		}

		EventPacket eventPacket = new EventPacket(){
			data = data,
			timestamp = timestamp,
			prevEventHash = hash,
		};

		currentChainEnd[keyChain] = eventPacket;

		AppendEventToFile(eventPacket, keyChain);
	}

	public void LoadEventChain(string keyChain){
		FileAccess eventFile = FileAccess.Open(chainStorePath + "/" + keyChain + ".chain", FileAccess.ModeFlags.Read);

		byte[] eventChainRaw = eventFile.GetBuffer((long)eventFile.GetLength());
		byte[][] dataspans = Bugcord.ReadDataSpans(eventChainRaw, 0);

		EventPacket eventPacket = new EventPacket();

		int section = 0;
		for (int i = 0; i < dataspans.Length; i++){
			switch (section)
			{
				case 0:
					eventPacket.data = dataspans[i];
					break;
				case 1:
					eventPacket.timestamp = BitConverter.ToDouble(dataspans[i]);
					break;
				case 2:
					eventPacket.prevEventHash = dataspans[i];
					break;
				default:
					section = 0;
					bugcord.ProcessIncomingPacket((PacketService.Packet)eventPacket);
					eventPacket = new EventPacket();
					break;
			}
			
			section++;
		}
	}

	public void AppendEventToFile(EventPacket packet, string keyChain){
		GD.Print("saving: " + chainStorePath + keyChain + ".chain");

		if (!FileAccess.FileExists(chainStorePath + keyChain + ".chain")){
			FileAccess.Open(chainStorePath + keyChain + ".chain", FileAccess.ModeFlags.Write);
		}

		List<byte> packetSection = new List<byte>();

		packetSection.AddRange(Bugcord.MakeDataSpan(packet.data));
		packetSection.AddRange(Bugcord.MakeDataSpan(BitConverter.GetBytes(packet.timestamp)));
		packetSection.AddRange(Bugcord.MakeDataSpan(packet.prevEventHash));

		FileAccess eventFile = FileAccess.Open(chainStorePath + keyChain + ".chain", FileAccess.ModeFlags.ReadWrite);

		eventFile.SeekEnd();
		eventFile.StoreBuffer(packetSection.ToArray());
		eventFile.Close();
	}

	// public bool VerifyEvent(EventPacket eventPacket, EventPacket previous){
	// 	List<byte> hashConcat = new List<byte>();
	// 	hashConcat.AddRange(previous.data);
	// 	hashConcat.AddRange(BitConverter.GetBytes(previous.timestamp));
	// 	hashConcat.AddRange(previous.hash);

	// 	byte[] prevHash = KeyService.GetSHA256Hash(hashConcat.ToArray());

	// 	hashConcat = new List<byte>();
	// 	hashConcat.AddRange(eventPacket.data);
	// 	hashConcat.AddRange(BitConverter.GetBytes(eventPacket.timestamp));
	// 	hashConcat.AddRange(eventPacket.hash);

	// 	byte[] hash = KeyService.GetSHA256Hash(hashConcat.ToArray());

	// 	return hash == eventPacket.hash;
	// }

	public class EventPacket{
		public byte[] data;
		public double timestamp;
		public byte[] prevEventHash;

		public static explicit operator PacketService.Packet(EventPacket eventPacket){
			PacketService.Packet packet = new PacketService.Packet(){
				timestamp = eventPacket.timestamp,
				data = eventPacket.data,
			};

			return packet;
		}
	}
}
