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

	// Gets a file from serve folder or cache or peer (returns true). If it cannot be found, a request to peers is made (returns false).
	public bool GetFile(string id, out byte[] data){
		GD.Print("getting file " + id);
		if (IsFileInCache(id)){
			data = GetFromCache(id);
			return true;
		}

		if (HasServableFile(id)){
			bool success = TransformServefile(GetServableData(id), out byte[] fileData, out string fileId, out string filename);
			if (success)
				WriteToCache(fileData, filename, fileId);
			data = fileData;
			return success;
		}

		bugcord.Send(bugcord.BuildFileRequest(id)); // Request file from peers

		data = null;
		return false;
	}

	public void UpdateFileBuffer(ushort filePart, ushort filePartMax, string fileId, string senderId, byte[] file){
		if (IsFileInCache(fileId)){ // File already in cache
			return;
		}

		if (HasServableFile(fileId)){ // File already recieved
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
		return FileAccess.FileExists(dataServePath + guid + ".file");
	}

	public byte[] GetServableData(string guid){
		FileAccess file = FileAccess.Open(dataServePath + guid + ".file", FileAccess.ModeFlags.Read);
		return file.GetBuffer((long)file.GetLength());
	}

	public void MakeServePath(){
		if (!DirAccess.DirExistsAbsolute(dataServePath)){
			DirAccess cacheDir = DirAccess.Open("user://");
			cacheDir.MakeDir("serve");
		}
	}

	public void WriteToServable(byte[] data, string guid){
		MakeServePath();

		FileAccess serveCopy = FileAccess.Open(dataServePath + guid + ".file", FileAccess.ModeFlags.Write);
		serveCopy.StoreBuffer(data);

		serveCopy.Close();
	}

	public bool IsFileInCache(string fileId){
		return cacheIndex.ContainsKey(fileId);
	}

	public byte[] GetFromCache(string guid){
		FileAccess file = FileAccess.Open(cacheIndex[guid], FileAccess.ModeFlags.Read);
		return file.GetBuffer((long)file.GetLength());
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
