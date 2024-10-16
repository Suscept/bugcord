using Godot;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

public partial class KeyService : Node
{
	// PeerId, RSAKey
	public System.Collections.Generic.Dictionary<string, byte[]> peerKeys = new();
	// KeyId, AESKey
	public System.Collections.Generic.Dictionary<string, byte[]> myKeys = new();
	public RSA userAuthentication;

	public const string clientKeyPath = "user://client.pem";
	public const string oldClientKeyPath = "user://client.auth";

	public const string knownKeysPath = "user://keys.pem";
	public const string oldKnownKeysPath = "user://keys.auth";

	private SpaceService spaceService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		spaceService = GetParent().GetNode<SpaceService>("SpaceService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public byte[] EncryptKeyForPeer(string keyId, string peerId){
		return EncryptForPeer(myKeys[keyId], peerId);
	}

	public byte[] EncryptForPeer(byte[] data, string peerId){
		RSA inviteAuth = RSACryptoServiceProvider.Create(2048);
		inviteAuth.ImportRSAPublicKey(peerKeys[peerId], out int bytesRead);
		byte[] spaceKeyEncrypted = inviteAuth.Encrypt(data, RSAEncryptionPadding.Pkcs1);

		return spaceKeyEncrypted;
	}

	public byte[] EncryptWithKey(byte[] data, string keyId, byte[] initVector){
		return AESEncrypt(data, myKeys[keyId], initVector);
	}

	public byte[] DecryptWithKey(byte[] data, string keyId, byte[] initVector){
		return AESDecrypt(data, myKeys[keyId], initVector);
	}

	public byte[] EncryptWithSpace(byte[] data, string spaceId, byte[] initVector){
		return AESEncrypt(data, myKeys[spaceService.spaces[spaceId].keyId], initVector);
	}

	public byte[] DecryptWithSpace(byte[] data, string spaceId, byte[] initVector){
		return AESDecrypt(data, myKeys[spaceService.spaces[spaceId].keyId], initVector);
	}

	// Generates and adds a new key. Returns the keys id as its hash
	public string NewKey(){
		Aes key = Aes.Create();
		string keyGuid = GetSHA256HashString(key.Key);

		AddKey(keyGuid, key.Key);

		return keyGuid;
	}

	public void AddKey(string keyId, byte[] key){
		if (myKeys.ContainsKey(keyId)){
			GD.Print("Key already known");
			return;
		}

		myKeys.Add(keyId, key);
		KeysSaveToFile();
	}

	public void KeysSaveToFile(){
		if (!FileAccess.FileExists(knownKeysPath)){
			FileAccess keyList = FileAccess.Open(knownKeysPath, FileAccess.ModeFlags.Write);
			Godot.Collections.Dictionary<string, string> keyDict = new();
			keyList.StoreString(Json.Stringify(keyDict));
			keyList.Close();
		}

		FileAccess.SetHiddenAttribute(knownKeysPath, false); // File cannot be written to if hidden (godot bug?)
		FileAccess keyFile = FileAccess.Open(knownKeysPath, FileAccess.ModeFlags.Write);

		Godot.Collections.Dictionary keysB64 = new Godot.Collections.Dictionary();
		foreach (KeyValuePair<string, byte[]> entry in myKeys){
			keysB64.Add(entry.Key, Bugcord.ToBase64(entry.Value));
		}

		keyFile.Seek(0);
		keyFile.StoreLine(Json.Stringify(keysB64));
		keyFile.Close();
		FileAccess.SetHiddenAttribute(knownKeysPath, true);
	}

	public void KeysLoadFromFile(){
		if (!FileAccess.FileExists(knownKeysPath)){
			if (FileAccess.FileExists(oldKnownKeysPath)){
				GD.Print("Migrating known keys file...");
				DirAccess.RenameAbsolute(oldKnownKeysPath, knownKeysPath);
				FileAccess.SetHiddenAttribute(knownKeysPath, true);
			}else{
				return;
			}
		}

		if (!FileAccess.FileExists(knownKeysPath)){
			return;
		}
		
		FileAccess keyFile = FileAccess.Open(knownKeysPath, FileAccess.ModeFlags.Read);
		string keyFileRaw = keyFile.GetAsText();

		myKeys = new System.Collections.Generic.Dictionary<string, byte[]>();

		foreach (KeyValuePair<Variant, Variant> entry in (Godot.Collections.Dictionary)Json.ParseString(keyFileRaw)){
			myKeys.Add((string)entry.Key, Bugcord.FromBase64((string)entry.Value));
		}

		keyFile.Close();
	}

	public void NewUserAuth(){
		userAuthentication = new RSACryptoServiceProvider(2048);
	}

	public void AuthSaveToFile(){
		FileAccess newKey = FileAccess.Open(clientKeyPath, FileAccess.ModeFlags.Write);
		FileAccess.SetHiddenAttribute(clientKeyPath, true); // Make file hidden
		
		byte[] privateKey = userAuthentication.ExportRSAPrivateKey();

		newKey.StoreBuffer(privateKey);
		newKey.Close();
	}

	public bool AuthLoadFromFile(){
		if (!FileAccess.FileExists(clientKeyPath)){
			if (FileAccess.FileExists(oldClientKeyPath)){
				GD.Print("Migrating user key file...");
				DirAccess.RenameAbsolute(oldClientKeyPath, clientKeyPath);
				FileAccess.SetHiddenAttribute(clientKeyPath, true);
			}else{
				return false;
			}
		}

		FileAccess userKeyFile = FileAccess.Open(clientKeyPath, FileAccess.ModeFlags.Read);
		long keyLength = (long)userKeyFile.GetLength();

		userAuthentication = new RSACryptoServiceProvider(2048);
		userAuthentication.ImportRSAPrivateKey(userKeyFile.GetBuffer(keyLength), out int bytesRead);
		userKeyFile.Close();

		return true;
	}

	public byte[] SignData(byte[] data){
		byte[] signature = userAuthentication.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		return signature;
	}

	public static bool VerifySigniture(byte[] data, byte[] signature, byte[] signeeKey){
		RSA signetureVerifier = RSA.Create();
		signetureVerifier.ImportRSAPublicKey(signeeKey, out int bytesRead);
		
		return signetureVerifier.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
	}

	public static byte[] GetSHA256Hash(byte[] data){
		using (SHA256 sha = SHA256.Create()){
			return sha.ComputeHash(data);
		}
	}

	public static string GetSHA256HashString(byte[] data){
		return Buglib.BytesToHex(GetSHA256Hash(data));
	}

	public static byte[] AESEncrypt(byte[] plaintext, byte[] key, byte[] iv){
		using (Aes aes = Aes.Create()){
			aes.Key = key;
			aes.IV = iv;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;

			using (ICryptoTransform encryptor = aes.CreateEncryptor()){
				return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
			}
		}
	}

	public static byte[] AESDecrypt(byte[] cyphertext, byte[] key, byte[] iv){
		using (Aes aes = Aes.Create()){
			aes.Key = key;
			aes.IV = iv;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;

			using (ICryptoTransform decryptor = aes.CreateDecryptor()){
				return decryptor.TransformFinalBlock(cyphertext, 0, cyphertext.Length);
			}
		}
	}

	public static byte[] GetRandomBytes(int length){
		byte[] bytes = new byte[length];
		new Random().NextBytes(bytes);
		
		return bytes;
	}
}
