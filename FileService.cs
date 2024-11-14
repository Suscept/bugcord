using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class FileService : Node
{
	[Signal] public delegate void OnCacheChangedEventHandler(string fileId);

	public const string cachePath = "user://cache/";
	public const string dataServePath = "user://serve/";

	public const ushort packageVersion = 1;

	// File ID, CacheFile
	public Dictionary<string, CacheFile> cacheIndex = new();

	private Bugcord bugcord;
	private KeyService keyService;
	private RequestService requestService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		bugcord = GetParent<Bugcord>();
		keyService = GetParent().GetNode<KeyService>("KeyService");
		requestService = GetParent().GetNode<RequestService>("RequestService");

		LoadCache();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	// Gets a file from serve folder or cache or peer (returns true). If it cannot be found, a request to peers is made and the file can be brought from cache later (returns false).
	public bool GetFile(string id, out byte[] data){
		GD.Print("getting file " + id);
		if (IsFileInCache(id)){
			GD.Print("Getting from cache");
			data = GetCacheData(id);
			return true;
		}

		if (IsFileServable(id)){
			GD.Print("Getting from package");
			bool success = UnpackageFile(GetServableData(id), out byte[] fileData, out string filename);
			if (success)
				WriteToCache(fileData, filename, id);
			data = fileData;
			return success;
		}

		GD.Print("Getting from network");
		requestService.Request(id, RequestService.FileExtension.MediaFile, RequestService.VerifyMethod.HashCheck);
		// bugcord.Send(bugcord.BuildFileRequest(id)); // Request file from peers

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
		if (version != packageVersion){
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
			if (!filename.IsValidFileName()){
				file = null;
				filename = null;
				return false;
			}

			file = decryptedDataspans[1];
			return true;
		}else{
			byte[][] dataSpans = Bugcord.ReadDataSpans(package, 3);

			filename = dataSpans[0].GetStringFromUtf8();
			file = dataSpans[1];
			return true;
		}
	}

	public bool IsFileServable(string guid){
		return IsFileServable(guid, RequestService.FileExtension.MediaFile);
	}

	public bool IsFileServable(string guid, RequestService.FileExtension extension){
		return FileAccess.FileExists(dataServePath + guid + RequestService.GetFileExtensionString(extension));
	}

	public byte[] GetServableData(string guid){
		return GetServableData(guid, RequestService.FileExtension.MediaFile);
	}

	public byte[] GetServableData(string guid, RequestService.FileExtension extension){
		FileAccess file = FileAccess.Open(dataServePath + guid + RequestService.GetFileExtensionString(extension), FileAccess.ModeFlags.Read);
		return file.GetBuffer((long)file.GetLength());
	}

	public void WriteToServable(byte[] data, string guid, RequestService.FileExtension extension){
		WriteToServableAbsolute(data, guid + RequestService.GetFileExtensionString(extension));
	}

	public void WriteToServable(byte[] data, string guid){
		WriteToServable(data, guid, RequestService.FileExtension.MediaFile);
	}

	public void WriteToServableAbsolute(byte[] data, string filename){
		MakeServePath();

		FileAccess serveCopy = FileAccess.Open(dataServePath + filename, FileAccess.ModeFlags.Write);
		serveCopy.StoreBuffer(data);

		serveCopy.Close();
	}

	public void MakeServePath(){
		if (!DirAccess.DirExistsAbsolute(dataServePath)){
			DirAccess cacheDir = DirAccess.Open("user://");
			cacheDir.MakeDir("serve");
		}
	}

	/// <summary>
	/// Returns the path to the file with the provided id. Returns null if the file does not exist.
	/// </summary>
	/// <param name="fileId"></param>
	/// <returns></returns>
	public string GetCachePath(string fileId){
		if(cacheIndex.TryGetValue(fileId, out CacheFile file)){
			return file.path;
		}
		return null;
	}

	public bool IsFileInCache(string fileId){
		return cacheIndex.ContainsKey(fileId);
	}

	public byte[] GetCacheData(string guid){
		FileAccess file = FileAccess.Open(cacheIndex[guid].path, FileAccess.ModeFlags.Read);
		return file.GetBuffer((long)file.GetLength());
	}

	public void WriteToCache(byte[] data, string filename, string guid){
		if (!DirAccess.DirExistsAbsolute(cachePath)){
			DirAccess cacheDir = DirAccess.Open("user://");
			cacheDir.MakeDir("cache");
		}

		string path = cachePath + guid + "." + filename.Split('.')[1];

		GD.Print("Writing to cache " + path);

		FileAccess cacheCopy = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		cacheCopy.StoreBuffer(data);

		cacheCopy.Close();

		CacheFile cacheFile = new CacheFile{
			id = guid,
			path = path,
			filename = filename,
		};
		cacheIndex.Add(guid, cacheFile);

		EmitSignal(SignalName.OnCacheChanged, guid);
	}

	public void ClearCache(){
		GD.Print("Clearing cache..");
		foreach (CacheFile file in cacheIndex.Values){
			DirAccess.RemoveAbsolute(file.path);
		}
		cacheIndex.Clear();
	}

	// Indexes the cache. Since the cache is cleared on shutdown this is mainly to handle unexpected shutdowns
	public void LoadCache(){
		GD.Print("FileService: Loading cache...");

		string[] filesInCache = DirAccess.GetFilesAt(cachePath);
		foreach (string file in filesInCache){
			CacheFile cacheFile = new CacheFile{
			id = file.Split('.')[0],
			path = cachePath + file,
			filename = "unknown." + file.Split('.')[1],
		};

			cacheIndex.Add(file.Split('.')[0], cacheFile);
		}
	}

	// Copies the file with its original filename to the user's downloads folder
	// File must be in the cache already
	public void DownloadFile(string id){
		if (!cacheIndex.ContainsKey(id))
			return;

		GD.Print("FileService: Downloading: " + id);

		string downloadsFolder = System.Environment.ExpandEnvironmentVariables("%userprofile%/downloads/");
		CacheFile file = cacheIndex[id];

		FileAccess downloadFile = FileAccess.Open(downloadsFolder + file.filename, FileAccess.ModeFlags.Write);
		downloadFile.StoreBuffer(GetCacheData(id));
		downloadFile.Close();

		// Opens windows file explorer to the downloads folder. The path from the env var needs its "/" replaced with "\" to work
		Process.Start("explorer.exe", downloadsFolder.Replace('/', '\\')); 
	}

	public class StoredPacket{
		public string packet;
		public double timestamp;
	}

	public class CacheFile{
		public string id;
		public string path;
		public string filename;
	}
}
