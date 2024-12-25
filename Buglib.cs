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
	/// Gets the human-readable size of some data. (1000B > 1KB)
	/// In regaurds to the confuzing world of KiB vs KB, This function will do as the Windows file explorer does and will use KiB while labelling as "KB"
	/// </summary>
	/// <param name="size">The data amount in bytes</param>
	/// <returns></returns>
	public static string ShortenDataSize(long size){
		if (size > 500){ // Kilobyte
			return (size/1000).ToString() + "KB";
		}else if(size > 500000){ // Megabyte
			return (size/1000000).ToString() + "MB";
		}else if(size > 500000000){ // Gigabyte
			return (size/1000000000).ToString() + "GB";
		}
		return size.ToString() + "B";
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

	public static string BytesToHex(byte[] bytes, string seperator){
		StringBuilder builder = new StringBuilder();

		foreach (byte b in bytes){
			builder.Append(b.ToString("x2") + seperator);
		}

		string finalString = builder.ToString();

		return finalString.Substring(0, finalString.Length - seperator.Length); // Cut off the last seperator
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

	/// <summary>
	/// Converts a byte array to a hex string. Contains no spaces between the hex values so it can be easily copied to tools like hexed.it
	/// </summary>
	/// <param name="data"></param>
	/// <returns>The bytes converted to a hexadecimal string</returns>
	public static string ToHexString(byte[] data){
		return BitConverter.ToString(data).Replace("-", "");
	}

	#region Dataspans

	public static byte[] ReadDataSpan(byte[] fullSpan, int startIndex){
		ushort spanLength = BitConverter.ToUInt16(fullSpan, startIndex);

		if (spanLength == 65535){ // A dataspan length of zero assumes its a very large span at the end of the data
			return ReadLengthInfinitely(fullSpan, startIndex + 2);
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
	public static byte[] ReadLengthInfinitely(byte[] data, int startIndex){
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
		return MakeDataSpan(data, false);
	}

	public static byte[] MakeDataSpan(byte[] data,  bool infiniteLength){
		if (infiniteLength){
			return MakeDataSpan(data, 65535);
		}
		if (data.Length >= 65535)
			GD.PushError("Attempting to create a dataspan with a length of more than 65534. Consider setting the infiniteLength override");
		return MakeDataSpan(data, (ushort)data.Length);
	}

	private static byte[] MakeDataSpan(byte[] data, ushort dataLength){
		List<byte> bytes = new List<byte>();

		byte[] lengthHeader = BitConverter.GetBytes(dataLength);

		bytes.AddRange(lengthHeader);
		bytes.AddRange(data);

		return bytes.ToArray();
	}

	#endregion
}
