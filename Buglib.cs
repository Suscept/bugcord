using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

public partial class Buglib : Node
{
	private static readonly char[] hexChars = {'0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f'};
	private static readonly string[] disallowedFilenames = {"index.json"};

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	/// <summary>
	/// Validates if the provided filename can be safely saved to disk
	/// </summary>
	/// <returns>True if the filename is allowed</returns>
	public static bool VerifyFilename(string filename){
		if (!filename.IsValidFileName())
			return false;

		foreach (string notAllowedName in disallowedFilenames){
			if (filename == notAllowedName)
				return false;
		}

		return true;
	}

	// String conversion from https://stackoverflow.com/a/17001289
	public static string BytesToHex(byte[] bytes){
		StringBuilder builder = new StringBuilder();

		foreach (byte b in bytes){
			builder.Append(b.ToString("x2"));
		}

		return builder.ToString();
	}

	/// <summary>
	/// Can be used to verify all hash-based id's across bugcord. Basically, if this function returns false than it's not a hash based id.
	/// </summary>
	/// <param name="hex"></param>
	/// <returns>true if the provided string is a valid hex string</returns>
	public static bool VerifyHexString(string hex){
		string lowerHex = hex.ToLower();

		if (lowerHex.Length % 2 != 0)
			return false; // String must be even
		
		for (int i = 0; i < lowerHex.Length; i++){
			bool found = false;
			for (int c = 0; c < hexChars.Length; c++){
				if (lowerHex[i] == hexChars[c]){
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
            gubgub = "bugcordee"
        };

		Godot.Collections.Dictionary keyValuePairs = ToGodotDict(gubehbs);
        // GD.Print(Json.Stringify(keyValuePairs));

		Testinger gorbag = new();

		// GD.Print(FromGodotDict<Testinger>(keyValuePairs, gorbag).gubgub);
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

	#region Dataspans
	public static byte[] ReadDataSpan(byte[] fullSpan, int startIndex){
		ushort spanLength = BitConverter.ToUInt16(fullSpan, startIndex);

		if (spanLength == 0){ // A dataspan length of zero assumes its a very large span at the end of the data
			return ReadLengthInfinetly(fullSpan, startIndex + 2);
		}

		return ReadLength(fullSpan, startIndex + 2, spanLength);
	}

	public static byte[][] ReadDataSpans(byte[] fullData, int startIndex){
		List<byte[]> spans = new List<byte[]>();
		for (int i = startIndex; i < fullData.Length; i += 0){ // increment not needed
			byte[] gotSpan = ReadDataSpan(fullData, i);
			spans.Add(gotSpan);
			i += gotSpan.Length + 2; // +2 accounts for length header
		}

		return spans.ToArray();
	}

	/// <summary>
	/// Reads all data after a specified index.
	/// </summary>
	/// <param name="data"></param>
	/// <param name="startIndex"></param>
	/// <returns></returns>
	public static byte[] ReadLengthInfinetly(byte[] data, int startIndex){
		int length = data.Length - startIndex;

		byte[] read = new byte[length];
		
		for (int i = 0; i < length; i++){
			read[i] = data[i + startIndex];
		}

		return read;
	}

	public static byte[] ReadLength(byte[] data, int startIndex, int length){
		if (data.Length < (startIndex + length)){
			GD.PrintErr("Dataspan reading failed. Index out of range. Length: " + length);
			throw new IndexOutOfRangeException();
		}

		byte[] read = new byte[length];
		
		for (int i = 0; i < length; i++){
			read[i] = data[i + startIndex];
		}

		return read;
	}

	public static byte[] MakeDataSpan(byte[] data){
		if (data.Length > 32767)
			GD.PrintErr("Attempting to create a dataspan with a length of more than 32767. Consider overriding length to zero if this span is the last of a set.");
		return MakeDataSpan(data, (short)data.Length);
	}

	public static byte[] MakeDataSpan(byte[] data, short lengthHeaderOverride){
		List<byte> bytes = new List<byte>();
		short dataLength = lengthHeaderOverride;

		byte[] lengthHeader = BitConverter.GetBytes(dataLength);

		bytes.AddRange(lengthHeader);
		bytes.AddRange(data);

		return bytes.ToArray();
	}

	#endregion
}
