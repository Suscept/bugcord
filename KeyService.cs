using Godot;
using System;
using System.Security.Cryptography;

public partial class KeyService : Node
{
	// KeyId, AESKey
	public System.Collections.Generic.Dictionary<string, byte[]> myKeys = new();

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
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
