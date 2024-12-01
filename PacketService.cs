using Godot;
using System;
using System.Collections.Generic;

// Handles everything going through TCP
public partial class PacketService : Node
{
	public const ushort packetVersion = 0;
	public const int defaultPort = 25987;

	[Signal] public delegate void OnConnectedEventHandler();

	[Export] public int packetsSentPerFrame = 1;
	[Export] public int packetsProcessedPerFrame = 10;

	public List<byte> incomingPacketBuffer = new List<byte>();
	public List<byte[]> outgoingPacketBuffer = new List<byte[]>();

	public StreamPeerTcp.Status currentState;

	public enum PacketType{
		Message = 0,
		Identify = 1,
		KeyPackage = 4,
		FileRequest = 6,
		FilePacket = 7,
		SpaceUpdate = 9,
		VoiceChatEvent = 11,
		FileAvailable = 12,
	}

	private StreamPeerTcp tcpClient = new StreamPeerTcp();

	private StreamPeerTcp.Status previousState;

	private Bugcord bugcord;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		bugcord = GetParent<Bugcord>();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		UpdateConnectionState();
		if (currentState == StreamPeerTcp.Status.Connected && currentState != previousState){
			EmitSignal(SignalName.OnConnected);
		}
		
		bool isConnected = currentState == StreamPeerTcp.Status.Connected;

		if (isConnected){
			DoDataLoop(); // Process everything
		}

		previousState = currentState;
	}

	public void SendPacket(byte[] data){
		outgoingPacketBuffer.Add(data);
	}

	public void Connect(string host, int port){
		GD.Print("Connecting to: " + host + " " + port);
		tcpClient.ConnectToHost(host, port);
	}

	public void Disconnect(){
		GD.Print("Disconnecting...");
		tcpClient.DisconnectFromHost();
	}

	public void UpdateConnectionState(){
		tcpClient.Poll();

		currentState = tcpClient.GetStatus();
	}

	public static bool ParseUrl(string url, out string host, out int port){
		string[] urlSplit = url.Split(":");
		string urlHost = urlSplit[0];
		int urlPort = defaultPort;
		if (urlSplit.Length > 1){ // If the port was included in the url
			urlPort = int.Parse(urlSplit[1]);
		}

		host = urlHost;
		port = urlPort;
		return true;
	}

	private void DoDataLoop(){
		// Process incoming
		if (tcpClient.GetAvailableBytes() > 0){
			Godot.Collections.Array recieved = tcpClient.GetData(tcpClient.GetAvailableBytes());
			byte[] rawPacket = (byte[])recieved[1];
			incomingPacketBuffer.AddRange(rawPacket);

			for (int i = 0; i < packetsProcessedPerFrame; i++){ // Process multiple packets at once
				bool processResult = UnpackagePacket(incomingPacketBuffer.ToArray(), out int usedPacketLength);
				incomingPacketBuffer.RemoveRange(0, usedPacketLength);

				if (!processResult) // Checksum faild. Packet likely has arrived partially
					break;
			}
		}

		// Process outgoing
		if (outgoingPacketBuffer.Count > 0){
			for (int i = 0; i < Mathf.Min(outgoingPacketBuffer.Count, packetsSentPerFrame); i++){
				SendRaw(PackagePacket(outgoingPacketBuffer[0]));
				outgoingPacketBuffer.RemoveAt(0);
			}
		}
	}

	private void SendRaw(byte[] data){
		tcpClient.PutData(data);
	}

	public byte[] PackagePacket(byte[] data){
		List<byte> packetBytes = new List<byte>();

		packetBytes.AddRange(BitConverter.GetBytes(packetVersion));

		byte[] checksum = Bugcord.GetChecksum(data);
		ushort packetLength = (ushort)data.Length;
		packetBytes.AddRange(checksum);
		packetBytes.AddRange(BitConverter.GetBytes(packetLength));
		packetBytes.AddRange(data);

		return packetBytes.ToArray();
	}

	public bool UnpackagePacket(byte[] data, out int usedPacketLength){
		usedPacketLength = 0;

		if (data.Length < 6){
			return false;
		}

		ushort version = BitConverter.ToUInt16(data, 0);
		short checksum = BitConverter.ToInt16(data, 2);
		ushort length = BitConverter.ToUInt16(data, 4);

		if (length > data.Length - 6){
			return false;
		}

		byte[] packetData = Bugcord.ReadLength(data, 6, length);

		if (!Bugcord.ValidateSumComplement(packetData, (ushort)checksum)){
			return false;
		}

		if (packetData.Length == 0){
			return false;
		}

		GD.Print("Checksum verified.");
		Packet packet = new Packet{
			timestamp = Time.GetUnixTimeFromSystem(),
			data = packetData,
			version = version,
		};

		try{
			bugcord.ProcessIncomingPacket(packet, false);
		}catch(Exception ex){
			GD.PrintErr(ex.Message);
			AlertPanel.PostAlert("Error", ex.Message, ex.StackTrace);
			usedPacketLength = length + 6;
			return true;
		}

		usedPacketLength = length + 6;
		return true;
	}

	public class Packet{
		public double timestamp;
		public byte[] data;
		public ushort version;
	}
}
