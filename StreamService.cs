using Godot;
using System;
using System.Collections.Generic;
using Concentus;

// Handle everything that goes over UDP
public partial class StreamService : Node
{
	public const int audioPacketDataSize = 4096;
	public const int opusFrameCount = 2880;

	[Export] public AudioStreamPlayer audioRecorder;
	[Export] public AudioStreamPlayer audioPlayer;

	[Export] public bool recieveVoice;
	[Export] public bool sendVoice;

	private PacketPeerUdp udpClient = new PacketPeerUdp();

	public static Dictionary<string, List<float>> incomingVoiceBuffer = new();
	private AudioEffectCapture recordBusCapture;
	private AudioStreamGeneratorPlayback voicePlaybackBus;

	private UserService userService;

	private IOpusEncoder opusEncoder;
	private IOpusDecoder opusDecoder;

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

		if (sendVoice && recieveVoice){ // You should only be able to send voice packets if you may also recieve them
			// Vector2[] vBuffer = GetVoiceBuffer(audioPacketDataSize);
			Vector2[] vBuffer = GetVoiceBuffer(opusFrameCount);

			if (vBuffer != null){
				Span<float> frames = stackalloc float[opusFrameCount];
				for (int i = 0; i < vBuffer.Length; i++){
					frames[i] = vBuffer[i][0];
				}
				byte[] encodedFrames = new byte[audioPacketDataSize];
				int encodedBytes = opusEncoder.Encode(frames, opusFrameCount, encodedFrames, audioPacketDataSize);

				List<byte> framesCut = new List<byte>();
				for (int i = 0; i < encodedBytes; i++){
					framesCut.Add(encodedFrames[i]);
				}

				udpClient.PutPacket(BuildVoicePacket(framesCut.ToArray()));
			}
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
		Vector2[] currentFrameAudio = new Vector2[opusFrameCount];

		// Loop over each voice stream from each peer
		foreach (KeyValuePair<string, List<float>> entry in incomingVoiceBuffer){
			if (entry.Key == userService.localPeer.id) // If this is our own voice stream
				continue;
			if (entry.Value.Count < opusFrameCount) // Stream too small
				continue;

			// // Streams are mixed by simply adding the voice frames recieved at the same time together
			for (int i = 0; i < currentFrameAudio.Length; i++){
				float f = entry.Value[i];
				vFrames++;
				currentFrameAudio[i] += new Vector2(f, f);
			}
			entry.Value.RemoveRange(0, opusFrameCount);
		}
		
		if (vFrames > 0)
			voicePlaybackBus.PushBuffer(currentFrameAudio);
	}

	public void Connect(string host, int port){
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

		opusEncoder = OpusCodecFactory.CreateEncoder(48000, 1);
		opusDecoder = OpusCodecFactory.CreateDecoder(48000, 1);
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
		byte[][] dataSpans = Buglib.ReadDataSpans(packet, 0);

		string senderId = dataSpans[0].GetStringFromUtf8();
		byte[] framesEncoded = dataSpans[1];

		float[] decodedFrames = new float[opusFrameCount];
		int decodedFrameCount = opusDecoder.Decode(framesEncoded, decodedFrames, opusFrameCount);
		List<float> framesCut = new List<float>();
		for (int i = 0; i < decodedFrameCount; i++){
			framesCut.Add(decodedFrames[i]);
		}

		incomingVoiceBuffer.TryAdd(senderId, new List<float>());
		incomingVoiceBuffer[senderId].AddRange(framesCut);
	}

	private byte[] BuildVoicePacket(byte[] audioFrames){
		List<byte> packetBytes = new List<byte>();

		packetBytes.AddRange(Buglib.MakeDataSpan(userService.localPeer.id.ToUtf8Buffer()));
		packetBytes.AddRange(Buglib.MakeDataSpan(audioFrames, 0));

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
