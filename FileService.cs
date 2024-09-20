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

	// Gets a file from serve folder or cache or peer (returns true). If it cannot be found, a request to peers is made and the file can be brought from cache later (returns false).
	public bool GetFile(string id, out byte[] data){
		GD.Print("getting file " + id);
		if (IsFileInCache(id)){
			data = GetFromCache(id);
			return true;
		}

		if (HasServableFile(id)){
			bool success = UnpackageFile(GetServableData(id), out byte[] fileData, out string filename);
			if (success)
				WriteToCache(fileData, filename, id);
			data = fileData;
			return success;
		}

		bugcord.Send(bugcord.BuildFileRequest(id)); // Request file from peers

		data = null;
		return false;
	}

	// Cache and make a serve package for a file at the given path. Returns the file's id
	public string PrepareFile(string path, bool encrypt, string keyId){
		string filename = System.IO.Path.GetFileName(path);

		GD.Print("preparing embedded file " + filename);
		
		FileAccess embedFile = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		byte[] embedData = embedFile.GetBuffer((long)embedFile.GetLength());
		byte[] servableData = PackageFile(embedData, filename, encrypt, keyId, out byte[] fileHash);

		string guidString = Buglib.BytesToHex(fileHash);

		WriteToCache(embedData, filename, guidString);
		WriteToServable(servableData, guidString);

		return guidString;
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

		string computedFileId = KeyService.GetSHA256HashString(fullFile.ToArray());
		if (fileId != computedFileId){
			GD.PrintErr("File hash mismatch! Packet: " + fileId + " Actual: " + computedFileId);
			return;
		}

		WriteToServable(fullFile.ToArray(), fileId);
		bool success = UnpackageFile(fullFile.ToArray(), out byte[] rawFile, out string filename);
		if (success)
			WriteToCache(rawFile, filename, fileId);
	}

	public byte[] PackageFile(byte[] file, string filename, bool encrypted, string encryptKeyId, out byte[] fileHash){
		List<byte> packagedFileData = new List<byte>
        {
            1, // Version (Little endian)
            0,
            encrypted ? (byte)1 : (byte)0 // Encryption flag
        };

		if (encrypted){
			byte[] iv = KeyService.GetRandomBytes(16);

			packagedFileData.AddRange(iv);
			packagedFileData.AddRange(Bugcord.MakeDataSpan(Bugcord.selectedKeyId.ToUtf8Buffer()));

			// Begin encrypted section
			List<byte> encryptSection = new List<byte>();

			encryptSection.AddRange(Bugcord.MakeDataSpan(filename.ToUtf8Buffer()));
			encryptSection.AddRange(Bugcord.MakeDataSpan(file, 0)); // Override length header to signify infinite length

			byte[] encryptedData = keyService.EncryptWithKey(encryptSection.ToArray(), encryptKeyId, iv);
			packagedFileData.AddRange(Bugcord.MakeDataSpan(encryptedData, 0));
		}else{
			packagedFileData.AddRange(Bugcord.MakeDataSpan(filename.ToUtf8Buffer()));
			packagedFileData.AddRange(Bugcord.MakeDataSpan(file, 0));
		}

		fileHash = KeyService.GetSHA256Hash(packagedFileData.ToArray());
		return packagedFileData.ToArray();
	}

	public bool UnpackageFile(byte[] package, out byte[] file, out string filename){
		ushort version = BitConverter.ToUInt16(package, 0);
		if (version != 1){
			file = null;
			filename = null;
			return false;
		}

		bool encrypted = package[2] == 1;

		if (encrypted){
			byte[] iv = Bugcord.ReadLength(package, 3, 16);
			byte[][] dataSpans = Bugcord.ReadDataSpans(package, 19);
			string keyId = dataSpans[0].GetStringFromUtf8();

			if (!keyService.myKeys.ContainsKey(keyId)){
				file = null;
				filename = null;
				return false;
			}

			byte[] encryptedSection = dataSpans[1];
			byte[][] decryptedDataspans = Bugcord.ReadDataSpans(keyService.DecryptWithKey(encryptedSection, keyId, iv), 0);
			filename = decryptedDataspans[0].GetStringFromUtf8();

			file = decryptedDataspans[1];
			return true;
		}else{
			byte[][] dataSpans = Bugcord.ReadDataSpans(package, 3);

			filename = dataSpans[0].GetStringFromUtf8();
			file = dataSpans[1];
			return true;
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
