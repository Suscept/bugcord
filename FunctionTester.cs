using Godot;
using System;
using System.Security.Cryptography;

public partial class FunctionTester : Panel
{
	[Export] public int type;

	[Export] public Bugcord bugcordInstance;

	[Export] public LineEdit input;
	[Export] public LineEdit input2;
	[Export] public LineEdit input3;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void OnInput(){
		switch (type)
		{
			case 0:
				foreach (byte[] b in Buglib.ReadDataSpans(Bugcord.FromBase64(input.Text), 0)){
					foreach (byte bt in b){
						GD.Print(bt);
					}
				}
				break;
			
			case 1:
				byte[] spaceKey = Bugcord.FromBase64(input.Text);
				
				RSA inviteAuth = RSACryptoServiceProvider.Create(2048);
				inviteAuth.ImportRSAPublicKey(Bugcord.FromBase64("MIIBCgKCAQEAlIsJe9h8tZ+YY1TcKfjx4XOBu75SPLjX7TYE7vrfV3C5oIwVMnFfqvm/c7gwCREQb439Au9fW8ZOgeqSK2yx43HrPusUz4TR+fip6D17odWK2sPgZjEAnscxVKygH+WwngwqkMhDGE+MaDlLleTRI1nHCyvw8tZm8lWlTgVK8GAXIU2Ilm81ybDU3fKAs0MGd2/rUgwMkVIiklIhYfQU58zCBBwV+u8XPv8fyJdha4HsJHqprEiwRjTMKa3qge12cXIfEH4a95hdYfFl5PIz0ZdJ4qtsOr+ZfmsfqfW8GMYvECAIRh7hSwhU+EfzIoA9Rlw211YwLfcnvQG8e9DDQQIDAQAB"), out int bytesRead);

				//inviteAuth.ImportRSAPublicKey(Bugcord.FromBase64(input2.Text), out int bytesRead);
				byte[] spaceKeyEncrypted = inviteAuth.Encrypt(spaceKey, RSAEncryptionPadding.Pkcs1);
				GD.Print(Bugcord.ToBase64(spaceKeyEncrypted));

				byte[] decryo = inviteAuth.Decrypt(spaceKeyEncrypted, RSAEncryptionPadding.Pkcs1);
				// byte[] decryo = inviteAuth.Decrypt(spaceKeyEncrypted, RSAEncryptionPadding.Pkcs1);

				GD.Print(Bugcord.ToBase64(decryo));
				
				break;

			case 2:
				bugcordInstance.DEBUGB64SpaceInvite(input.Text);

				break;
		}

	}
}
