using Godot;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;

public partial class NotificationService : Node
{
	[Export] public AudioStream singleNotificationSound;

	public enum NotificationType{
		Single = 0,
		Tune = 1,
	}

	private AudioStreamPlayer streamPlayer;
	private bool notifyLoopActive;

	private UserService userService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		streamPlayer = GetNode<AudioStreamPlayer>("NotificationPlayer");
		userService = GetParent().GetNode<UserService>("UserService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void ProcessNotificationPacket(byte[] packet){
		NotificationType type = (NotificationType)packet[0];
		byte users = packet[1];
		byte[][] userIds = Buglib.ReadDataSpans(packet, 2);

		if (users > userIds.Length)
			GD.PrintErr("NotificationService: Packet contains less users than expected. Expected: " + users + " Got: " + userIds.Length);

		for (int i = 0; i < users; i++){
			if (userIds[i].GetStringFromUtf8() != userService.localPeer.id)
				continue;

			switch (type)
			{
				case NotificationType.Single:
					SingleNotify();
					break;
				case NotificationType.Tune:
					StartNotifyLoop();
					break;
			}

			return;
		}
	}

	public byte[] BuildNotificationPacket(NotificationType notificationType, List<string> users){
		List<byte> packetList = new List<byte>();

		packetList.Add((byte)notificationType);

		if (users.Count > 256){
			GD.PrintErr("NotificationService: Attempting to notify more than 256 users.");
			packetList.Add(0); // Zero users
			return packetList.ToArray();
		}

		packetList.Add((byte)users.Count);

		for (int i = 0; i < users.Count; i++){
			packetList.AddRange(Buglib.MakeDataSpan(users[i].ToUtf8Buffer()));
		}

		return packetList.ToArray();
	}

	public void SingleNotify(){
		if (GetWindow().HasFocus())
			return;
		
		GetWindow().RequestAttention();
		
		streamPlayer.Stream = singleNotificationSound;
		streamPlayer.Play();
	}

	public void StartNotifyLoop(){
		if (notifyLoopActive)
			return;

		GetWindow().RequestAttention();
	}

	public void StopNotifyLoop(){
		if (!notifyLoopActive)
			return;
	}
}
