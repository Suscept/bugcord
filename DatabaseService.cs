using Godot;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

public partial class DatabaseService : Node
{
	public const string databasePath = "user://serve/messages/messageDatabase.db";

	public SqliteConnection sqlite;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		sqlite = new SqliteConnection("Data Source="+ProjectSettings.GlobalizePath(databasePath));
		sqlite.Open();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		
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
			gotMessages.Add(readingMessage);
		}

		return gotMessages;
	}

	public void SaveMessage(string spaceId, string id, string senderId, string content, string embedId, double unixTimestamp, ushort nonce){
		ExecuteSql(@$"
			INSERT INTO '{spaceId}'(messageId, userId, content, embedId, unixTimestamp, nonce)
			VALUES ($0, $1, $2, $3, $4, $5)
		", false, id, senderId, content, embedId, TimestampSecondsToMiliseconds(unixTimestamp), BitConverter.ToInt16(BitConverter.GetBytes(nonce)));
	}

	public void SaveMessage(string spaceId, Message message){
		ExecuteSql(@$"
			INSERT INTO '{spaceId}'(messageId, userId, content, embedId, unixTimestamp, nonce)
			VALUES ($0, $1, $2, $3, $4, $5)
		", false, message.id, message.senderId, message.content, message.embedId, TimestampSecondsToMiliseconds(message.unixTimestamp), BitConverter.ToInt16(BitConverter.GetBytes(message.nonce)));
	}

	public void AddSpaceTable(string spaceId){
		ExecuteSql($@"
			CREATE TABLE IF NOT EXISTS '{spaceId}' (
			messageId TEXT PRIMARY KEY,
			userId TEXT NOT NULL,
			content TEXT,
			embedId TEXT,
			unixTimestamp INTEGER,
			nonce INTEGER
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

		public static explicit operator Godot.Collections.Dictionary(Message message){
			Godot.Collections.Dictionary messageDict = new Godot.Collections.Dictionary
            {
                { "id", message.id },
                { "senderId", message.senderId },
                { "content", message.content },
                { "embedId", message.embedId },
                { "unixTimestamp", message.unixTimestamp },
                { "nonce", message.nonce },
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
            };
			return message;
		}
	}
}
