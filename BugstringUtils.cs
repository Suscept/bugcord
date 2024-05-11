using Godot;
using System;

public partial class BugstringUtils : Node
{
    public static string BytesToSizeString(ulong bytes){
        string byteCount = bytes.ToString();
        string sizeTag = "B";

        if (byteCount.Length > 12)
            sizeTag = "TB";


        return byteCount + sizeTag;
    }
}
