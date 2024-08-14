using Godot;
using System;

public partial class WebhookService : HttpRequest
{
	public string auth;

	private UserService userService;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		userService = GetParent().GetNode<UserService>("UserService");
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public void SendMessage(string content){
		string[] h = new string[]{
			"Content-Type: application/json"
		};
		Request(userService.webhookUrl, h, HttpClient.Method.Post, "{\"content\":\"" + content + "\"}");
	}

	public void _on_request_completed(long result, long responceCode, string[] headers, byte[] body){
		GD.Print("result: " + result);
		GD.Print("responceCode: " + responceCode);
		GD.Print("headers: " + headers);
		GD.Print("body: " + body.GetStringFromUtf8());
	}
}
