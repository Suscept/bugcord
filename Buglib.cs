using Godot;
using System;
using System.Reflection;
using System.Text;

public partial class Buglib : Node
{
	private static readonly char[] hexChars = {'0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f'};

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	// String conversion from https://stackoverflow.com/a/17001289
	public static string BytesToHex(byte[] bytes){
		StringBuilder builder = new StringBuilder();

		foreach (byte b in bytes){
			builder.Append(b.ToString("x2"));
		}

		return builder.ToString();
	}

	// Returns true if the provided string is a valid hex string
	// Can be used to verify all hash-based id's across bugcord. Basically, if this function returns false than it's not a hash based id.
	public static bool VerifyHexString(string hex){
		if (hex.Length % 2 != 0)
			return false; // String must be even
		
		for (int i = 0; i < hex.Length; i++){
			bool found = false;
			for (int c = 0; c < hexChars.Length; c++){
				if (hex[i] == hexChars[c]){
					found = true;
					break;
				}
			}
			if (!found) // This char is not a valid hex char
				return false;
		}

		return true;
	}

    public override void _EnterTree()
    {
        Testinger gubehbs = new()
        {
            gubgub = "queef"
        };

		Godot.Collections.Dictionary keyValuePairs = ToGodotDict(gubehbs);
        GD.Print(Json.Stringify(keyValuePairs));

		Testinger gorbag = new();

		GD.Print(FromGodotDict<Testinger>(keyValuePairs, gorbag).gubgub);
    }

	public class Testinger{
		public string gubgub {get; set;}
	}

    public static Godot.Collections.Dictionary ToGodotDict(object thing){
		Godot.Collections.Dictionary dict = new Godot.Collections.Dictionary();

		PropertyInfo[] properties = thing.GetType().GetProperties();
		foreach (PropertyInfo property in properties){
			if (property.GetValue(thing).GetType() == typeof(string)){
				dict.Add(property.Name, (string)property.GetValue(thing));
				continue;
			}
		}

		return dict;
	}

	public static T FromGodotDict<T>(Godot.Collections.Dictionary dict, object target){
		PropertyInfo[] properties = typeof(T).GetProperties();
		foreach (PropertyInfo property in properties){
			object type = property.GetValue(target);
			property.SetValue(target, (string)dict[property.Name]);
		}

		return (T)target;
	}

	// https://stackoverflow.com/a/57176946
	public static bool ObjectIsClass(object o)
	{
		return  o.GetType().GetConstructor(new Type[0])!=null;
	}
}
