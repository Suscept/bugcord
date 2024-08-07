using Godot;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

public partial class FileService : Node
{
	[Signal] public delegate void OnCacheChangedEventHandler(string fileId);
	[Signal] public delegate void OnFileBufferUpdatedEventHandler(string id, int partsHad, int partsTotal);

	public const string cachePath = "user://cache/";
	public const string dataServePath = "user://serve/";
	public const string packetStorePath = "user://serve/messages";

	// File ID, File path
	public Dictionary<string, string> cacheIndex = new();

	// File ID, {Sender ID, File Parts}
	public static Dictionary<string, Dictionary<string, List<byte[]>>> incomingFileBuffer = new();

	private Bugcord bugcord;
	private KeyService keyService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		bugcord = GetParent<Bugcord>();
		keyService = GetParent().GetNode<KeyService>("KeyService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void SavePacket(byte[] packet){
		if (!DirAccess.DirExistsAbsolute(cachePath)){
			DirAccess cacheDir = DirAccess.Open("user://");
			cacheDir.MakeDir("cache");
		}

		if (!DirAccess.DirExistsAbsolute(packetStorePath)){
			DirAccess cacheDir = DirAccess.Open("user://serve");
			cacheDir.MakeDir("messages");
		}

		if (!FileAccess.FileExists(packetStorePath+"/packets.jsonl")){
			FileAccess.Open(packetStorePath+"/packets.jsonl", FileAccess.ModeFlags.Write).Close();
		}

		string packetString = Bugcord.ToBase64(packet);
		StoredPacket stored = new StoredPacket{
			packet = packetString,
			timestamp = Time.GetUnixTimeFromSystem()
		};

		FileAccess file = FileAccess.Open(packetStorePath+"/packets.jsonl", FileAccess.ModeFlags.ReadWrite);
		file.SeekEnd();
		file.StoreLine(JsonConvert.SerializeObject(stored));
		file.Close();
	}

	public void LoadCatchup(){
		if (!FileAccess.FileExists(packetStorePath+"/packets.jsonl")){
			return;
		}

		FileAccess file = FileAccess.Open(packetStorePath+"/packets.jsonl", FileAccess.ModeFlags.Read);
		while (true){
			string jsonl = file.GetLine();
			if (jsonl == "")
				break;
			StoredPacket stored = JsonConvert.DeserializeObject<StoredPacket>(jsonl);
			Bugcord.catchupBuffer.Add(Bugcord.FromBase64(stored.packet));
		}
	}

	public void UpdateFileBuffer(ushort filePart, ushort filePartMax, string fileId, string senderId, byte[] file){
		if (IsFileInCache(fileId)){ // File already in cache
			return;
		}

		// Is the file known?
		if (!incomingFileBuffer.ContainsKey(fileId)){
            Dictionary<string, List<byte[]>> recievingFile = new();
            incomingFileBuffer.Add(fileId, recievingFile);
		}

		// Has this user sent parts before?
		if (!incomingFileBuffer[fileId].ContainsKey(senderId)){
			List<byte[]> bytesList = new List<byte[]>();
			byte[][] bytes = new byte[filePartMax][];
			bytesList.AddRange(bytes);
			incomingFileBuffer[fileId].Add(senderId, bytesList); 
		}
	
		incomingFileBuffer[fileId][senderId][filePart] = file;

		int filePartsRecieved = 0;
		int filePartsTotal = incomingFileBuffer[fileId][senderId].Count;
		for (int i = 0; i < filePartsTotal; i++){
			if (incomingFileBuffer[fileId][senderId][i] != null){
				filePartsRecieved++;
			}
		}

		EmitSignal(SignalName.OnFileBufferUpdated, fileId, filePartsRecieved, filePartsTotal);

		if (filePartsRecieved < filePartsTotal){
			return;
		}

		// No parts are missing so concatinate everything and save and cache
		List<byte> fullFile = new();
		for (int i = 0; i < incomingFileBuffer[fileId][senderId].Count; i++){
			fullFile.AddRange(incomingFileBuffer[fileId][senderId][i]);
		}
		incomingFileBuffer[fileId].Remove(senderId);

		WriteToServable(fullFile.ToArray(), fileId);
		bool canCache = TransformServefile(fullFile.ToArray(), out byte[] rawFile, out string trueFileId, out string filename);
		if (canCache)
			WriteToCache(rawFile, filename, trueFileId);
	}

	public byte[] TransformRealFile(byte[] file, string filename, string guid, bool encrypted){
		byte[] fileGuid = guid.ToUtf8Buffer();
		List<byte> serveCopyData = new List<byte>();

		if (encrypted){
			byte[] iv = KeyService.GetRandomBytes(16);
			byte[] encryptedData = keyService.EncryptWithSpace(file, Bugcord.selectedSpaceId, iv);

			serveCopyData.Add(0);

			serveCopyData.AddRange(Bugcord.MakeDataSpan(fileGuid));
			serveCopyData.AddRange(Bugcord.MakeDataSpan(iv));
			serveCopyData.AddRange(Bugcord.MakeDataSpan(Bugcord.selectedKeyId.ToUtf8Buffer()));
			serveCopyData.AddRange(Bugcord.MakeDataSpan(filename.ToUtf8Buffer()));
			serveCopyData.AddRange(Bugcord.MakeDataSpan(encryptedData, 0)); // cant use dataspans for this since the files length in bytes may be more than 2^16
		}else{
			serveCopyData.Add(1); // indicate no encryption

			serveCopyData.AddRange(Bugcord.MakeDataSpan(fileGuid));
			serveCopyData.AddRange(Bugcord.MakeDataSpan(filename.ToUtf8Buffer()));
			serveCopyData.AddRange(Bugcord.MakeDataSpan(file, 0));
		}

		return serveCopyData.ToArray();
	}

	public bool TransformServefile(byte[] servefile, out byte[] file, out string fileId, out string filename){
		byte[][] dataSpans = Bugcord.ReadDataSpans(servefile, 1);
		byte flags = servefile[0];

		switch (flags)
		{
			case 0: // Encrypted
				fileId = dataSpans[0].GetStringFromUtf8();
				string keyId = dataSpans[2].GetStringFromUtf8();
				filename = dataSpans[3].GetStringFromUtf8();

				if (!keyService.myKeys.ContainsKey(keyId)){
					file = null;
					return false;
				}

				byte[] key = keyService.myKeys[keyId];
				byte[] iv = dataSpans[1];

				byte[] decryptedData = KeyService.AESDecrypt(dataSpans[4], key, iv);

				file = decryptedData;
				return true;
			case 1: // Not encrypted file
				fileId = dataSpans[0].GetStringFromUtf8();
				filename = dataSpans[1].GetStringFromUtf8();
				file = dataSpans[2];
				return true;
			default:
				file = null;
				filename = null;
				fileId = null;
				return false;
		}
	}

	public bool HasServableFile(string guid){
		FileAccess file = FileAccess.Open(dataServePath + guid + ".file", FileAccess.ModeFlags.Read);
		if (file == null)
			return false;
		return true;
	}

	public byte[] GetServableData(string guid){
		FileAccess file = FileAccess.Open(dataServePath + guid + ".file", FileAccess.ModeFlags.Read);
		return file.GetBuffer((long)file.GetLength());
	}

	public void WriteToServable(byte[] data, string guid){
		if (!DirAccess.DirExistsAbsolute(dataServePath)){
			DirAccess cacheDir = DirAccess.Open("user://");
			cacheDir.MakeDir("serve");
		}

		FileAccess serveCopy = FileAccess.Open(dataServePath + guid + ".file", FileAccess.ModeFlags.Write);
		serveCopy.StoreBuffer(data);

		serveCopy.Close();
	}

	public bool IsFileInCache(string fileId){
		return cacheIndex.ContainsKey(fileId);
	}

	public void WriteToCache(byte[] data, string filename, string guid){
		if (!DirAccess.DirExistsAbsolute(cachePath)){
			DirAccess cacheDir = DirAccess.Open("user://");
			cacheDir.MakeDir("cache");
		}

		string path = cachePath + filename;

		GD.Print("Writing to cache " + path);

		FileAccess cacheCopy = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		cacheCopy.StoreBuffer(data);

		cacheCopy.Close();

		cacheIndex.Add(guid, path);

		EmitSignal(SignalName.OnCacheChanged, guid);
	}

	public void ClearCache(){
		GD.Print("Clearing cache..");
		foreach (string path in cacheIndex.Values){
			DirAccess.RemoveAbsolute(path);
		}
		cacheIndex.Clear();
	}

	public class StoredPacket{
		public string packet;
		public double timestamp;
	}
}
