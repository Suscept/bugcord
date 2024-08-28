using Godot;
using System;
using System.Collections.Generic;

// Handle everything that goes over UDP
public partial class StreamService : Node
{
	public const int audioPacketDataSize = 4096;

	[Export] public CheckBox voiceChatToggle;

	[Export] public AudioStreamPlayer audioRecorder;
	[Export] public AudioStreamPlayer audioPlayer;

	[Export] public bool recieveVoice;
	[Export] public bool sendVoice;

	private PacketPeerUdp udpClient;

	public static Dictionary<string, List<byte>> incomingVoiceBuffer = new();
	private AudioEffectCapture recordBusCapture;
	private AudioStreamGeneratorPlayback voicePlaybackBus;

	private UserService userService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		userService = GetParent().GetNode<UserService>("UserService");
		InitVoice();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (!udpClient.IsSocketConnected())
			return;

		sendVoice = recieveVoice = voiceChatToggle.ButtonPressed; // Temporary until dedicated mute/deafen buttons are added

		if (sendVoice && recieveVoice){ // You should only be able to send voice packets if you may also recieve them
			Vector2[] vBuffer = GetVoiceBuffer(audioPacketDataSize);
			if (vBuffer != null)
				udpClient.PutPacket(BuildVoicePacket(vBuffer));
		}

		if (recieveVoice){
			while (udpClient.GetAvailablePacketCount() > 0){
				ProcessVoicePacket(udpClient.GetPacket());
			}

			MixAndPlay();
		}
	}

	// Mix all audio streams into a single stream for playback
	public void MixAndPlay(){
		int vFrames = 0;
		Vector2[] currentFrameAudio = new Vector2[audioPacketDataSize];

		// Loop over each voice stream from each peer
		foreach (KeyValuePair<string, List<byte>> entry in incomingVoiceBuffer){
			if (entry.Key == userService.userId) // If this is our own voice stream
				continue;
			if (entry.Value.Count < audioPacketDataSize) // Stream too small
				continue;

			// Streams are mixed by simply adding the voice frames recieved at the same time together
			for (int i = 0; i < currentFrameAudio.Length; i++){
				float f = ByteToFloat(entry.Value[i]);
				vFrames++;
				currentFrameAudio[i] += new Vector2(f, f);
			}
			entry.Value.RemoveRange(0, audioPacketDataSize);
		}
		
		if (vFrames > 0)
			voicePlaybackBus.PushBuffer(currentFrameAudio);
	}

	public void Connect(string host, int port){
		udpClient = new PacketPeerUdp();
		udpClient.ConnectToHost(host, port);
	}

	public void InitVoice(){
		// Create the recorder
		AudioStreamMicrophone audioStreamMicrophone = new AudioStreamMicrophone();
		audioRecorder.Stream = audioStreamMicrophone;
		audioRecorder.Play();

		// Prepare to read and write data from the dedicated recording and playback buses
		int recordBusIndex = AudioServer.GetBusIndex("Record");
		
		recordBusCapture = (AudioEffectCapture)AudioServer.GetBusEffect(recordBusIndex, 0);
		voicePlaybackBus = (AudioStreamGeneratorPlayback)audioPlayer.GetStreamPlayback();
	}

	public Vector2[] GetVoiceBuffer(int bufferSize){
		int framesAvailable = recordBusCapture.GetFramesAvailable();
		
		if (framesAvailable >= bufferSize){
			Vector2[] frames = recordBusCapture.GetBuffer(bufferSize);
			return frames;
		}
		
		return null;
	}

	private void ProcessVoicePacket(byte[] packet){
		byte[][] dataSpans = Bugcord.ReadDataSpans(packet, 1);

		string senderId = dataSpans[0].GetStringFromUtf8();
		byte[] framesEncoded = dataSpans[1];

		incomingVoiceBuffer.TryAdd(senderId, new List<byte>());
		incomingVoiceBuffer[senderId].AddRange(framesEncoded);
	}

	private byte[] BuildVoicePacket(Vector2[] audioFrames){
		if (audioFrames.Length == 0)
			return new byte[0];

		List<byte> packetBytes = new List<byte>();

		byte[] codedFrames = new byte[audioFrames.Length];

		for (int i = 0; i < audioFrames.Length; i++){
			byte f = FloatToByte(audioFrames[i].X);

			codedFrames[i] = f;
		}

		packetBytes.AddRange(Bugcord.MakeDataSpan(userService.userId.ToUtf8Buffer()));
		packetBytes.AddRange(Bugcord.MakeDataSpan(codedFrames, 0));

		return packetBytes.ToArray();
	}

	public static float ByteToFloat(byte b){
		int bInt = b;
		return Mathf.Clamp(((float)(bInt + 1)/128) - 1, -1, 1);
	}

	public static byte FloatToByte(float f){
		float fUnsigned = f + 1;
		return (byte)(Mathf.FloorToInt(fUnsigned * 128) - 1);
	}

	public static float BytesToFloat(byte[] b, int index){
		// int bInt = b;
		return (float)BitConverter.ToSingle(b, index);
	}

	public static byte[] FloatToBytes(float f){
		float fUnsigned = f + 1;
		return BitConverter.GetBytes(f);
	}
}
