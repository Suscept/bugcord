using Godot;
using System;

public partial class LangFiles : Node
{
	public static Godot.Collections.Dictionary loadedLanguage = new();
	private static string loadedLanguageCode;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	/// <summary>
	/// Loads a language file into memory
	/// </summary>
	/// <param name="language">The language to be loaded. (Example: "en_us" for American English)</param>
	public static void LoadLanguage(string language){
		loadedLanguage = (Godot.Collections.Dictionary)Json.ParseString(FileAccess.Open("res://lang/"+language+".json", FileAccess.ModeFlags.Read).GetAsText());
		loadedLanguageCode = language;
	}

	/// <summary>
	/// Get a string from the currently loaded language. Falls back to en_us
	/// </summary>
	/// <param name="textIdentifier">The string to load. (Example: "register_welcome_title")</param>
	/// <returns>The string from the loaded language json. (Example: "Welcome to Bugcord!")</returns>
	public static string Get(string textIdentifier){
		if (loadedLanguageCode == null)
			LoadLanguage("en_us");

		bool textExists = loadedLanguage.TryGetValue(textIdentifier, out Variant text);
		if (!textExists){
			GD.PushError(loadedLanguageCode + " does not contain " + textIdentifier);
			return textIdentifier;
		}

		return (string)text;
	}
}
