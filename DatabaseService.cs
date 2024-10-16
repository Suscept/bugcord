using Godot;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;

public partial class DatabaseService : Node
{
	public const string packetStorePath = "user://serve/messages";
	public const string databasePath = "user://serve/messages/messageDatabase.db";
	public const string eventDatabasePath = "user://serve/messages/eventDatabase.db";

	public SqliteConnection sqlite;

	private FileService fileService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		fileService = GetParent().GetNode<FileService>("FileService");
		fileService.MakeServePath();

		MakePacketStorePath();

		sqlite = new SqliteConnection("Data Source="+ProjectSettings.GlobalizePath(databasePath));
		sqlite.Open();

		AddPacketTable();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		
	}

	public void MakePacketStorePath(){
		if (!DirAccess.DirExistsAbsolute(packetStorePath)){
			DirAccess cacheDir = DirAccess.Open("user://serve/");
			cacheDir.MakeDir("messages");
		}
	}

	public List<Message> GetMessages(string spaceId){
		return GetMessages(spaceId, 0);
	}

	public List<Message> GetMessages(string spaceId, uint afterDate){
		SqliteDataReader reader = GetSQLReader(@$"
			SELECT *
			FROM '{spaceId}'
			WHERE unixTimestamp > $0
		", (long)afterDate);

		List<Message> gotMessages = new List<Message>();
		while (reader.Read()){
			Message readingMessage = new Message{
				id = reader.GetString(0),
				senderId = reader.GetString(1),
				unixTimestamp = TimestampMilisecondsToSeconds(reader.GetInt64(4)),
				nonce = BitConverter.ToUInt16(BitConverter.GetBytes(reader.GetInt16(5))),
			};

			// These values may or may not be set
			if (!reader.IsDBNull(2))
				readingMessage.content = reader.GetString(2);
			if (!reader.IsDBNull(3))
				readingMessage.embedId = reader.GetString(3);
			if (!reader.IsDBNull(6))
				readingMessage.replyingTo = reader.GetString(6);
			gotMessages.Add(readingMessage);
		}

		return gotMessages;
	}

	public List<PacketService.Packet> GetPackets(long afterDate){
		SqliteDataReader reader = GetSQLReader(@$"
			SELECT *
			FROM packet_store
			WHERE unixTimestamp > $0
		", afterDate);

		List<PacketService.Packet> gotPackets = new List<PacketService.Packet>();
		while (reader.Read()){
			PacketService.Packet readingPacket = new PacketService.Packet{
				timestamp = TimestampMilisecondsToSeconds(reader.GetInt64(0)),
				data = (byte[])reader["packet"]
			};

			gotPackets.Add(readingPacket);
		}

		return gotPackets;
	}

	public void SavePacket(PacketService.Packet packet){
		ExecuteSql(@$"
			INSERT INTO packet_store(packet, unixTimestamp)
			VALUES ($0, $1)
		", false, packet.data, TimestampSecondsToMiliseconds(packet.timestamp));
	}

	public void SaveMessage(string spaceId, Message message){
		if (spaceId == "packet_store"){ // Prevent injection
			return;
		}

		ExecuteSql(@$"
			INSERT INTO '{spaceId}'(messageId, userId, content, embedId, unixTimestamp, nonce, replyingTo)
			VALUES ($0, $1, $2, $3, $4, $5, $6)
		", false, message.id, message.senderId, message.content, message.embedId, TimestampSecondsToMiliseconds(message.unixTimestamp), BitConverter.ToInt16(BitConverter.GetBytes(message.nonce)), message.replyingTo);
	}

	public void AddPacketTable(){
		ExecuteSql($@"
			CREATE TABLE IF NOT EXISTS packet_store (
			unixTimestamp INTEGER NOT NULL,
			packet BLOB NOT NULL
			)
		", false);
	}

	public void AddSpaceTable(string spaceId){
		ExecuteSql($@"
			CREATE TABLE IF NOT EXISTS '{spaceId}' (
			messageId TEXT PRIMARY KEY,
			userId TEXT NOT NULL,
			content TEXT,
			embedId TEXT,
			unixTimestamp INTEGER,
			nonce INTEGER,
			replyingTo TEXT
			)
		", false);
	}

	public string[] ExecuteSql(string sql, bool isQuery, params object[] args){
		SqliteCommand command = sqlite.CreateCommand();
		command.CommandText = sql;

		if (args != null){
			for (int i = 0; i < args.Length; i++){
				if (args[i] == null){
					command.Parameters.AddWithValue("$"+i, DBNull.Value);
					continue;
				}
				command.Parameters.AddWithValue("$"+i, args[i]);
			}
		}

		if (!isQuery){
			command.ExecuteNonQuery();
			return null;
		}

		List<string> data = new List<string>();

		SqliteDataReader reader = command.ExecuteReader();
		while (reader.Read()){
			data.Add(reader.GetString(0));
		}

		return data.ToArray();
	}

	public SqliteDataReader GetSQLReader(string sql, params object[] args){
		SqliteCommand command = sqlite.CreateCommand();
		command.CommandText = sql;

		if (args != null){
			for (int i = 0; i < args.Length; i++){
				if (args[i] == null){
					command.Parameters.AddWithValue("$"+i, DBNull.Value);
					continue;
				}
				command.Parameters.AddWithValue("$"+i, args[i]);
			}
		}

		return command.ExecuteReader();
	}

	public long TimestampSecondsToMiliseconds(double timestamp){
		return (long)Math.Floor(timestamp * 1000);
	}

	public double TimestampMilisecondsToSeconds(long timestamp){
		return timestamp / 1000d;
	}

	public class Message{
		public string id;
		public string senderId;
		public string content;
		public string embedId;
		public double unixTimestamp;
		public ushort nonce;
		public string replyingTo;

		public static explicit operator Godot.Collections.Dictionary(Message message){
			Godot.Collections.Dictionary messageDict = new Godot.Collections.Dictionary
            {
                { "id", message.id },
                { "senderId", message.senderId },
                { "content", message.content },
                { "embedId", message.embedId },
                { "unixTimestamp", message.unixTimestamp },
                { "nonce", message.nonce },
                { "replyingTo", message.replyingTo },
            };
			return messageDict;
		}

		public static explicit operator Message(Godot.Collections.Dictionary messageDict){
			Message message = new Message
            {
                id = (string)messageDict["id"],
                senderId = (string)messageDict["senderId"],
                content = (string)messageDict["content"],
                embedId = (string)messageDict["embedId"],
                unixTimestamp = (double)messageDict["unixTimestamp"],
                nonce = (ushort)messageDict["nonce"],
				replyingTo = (string)messageDict["replyingTo"],
            };
			return message;
		}
	}
}
