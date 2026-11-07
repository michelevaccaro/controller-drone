using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

public sealed class AxisBinding
{
    public int Offset, Center, Positive, Negative;

    internal double Read(byte[] frame)
    {
        int movement = MavicSerial.Value(frame, Offset) - Center;
        int positive = Positive - Center;
        int negative = Negative - Center;
        double value = Math.Sign(movement) == Math.Sign(positive)
            ? (double)movement / positive : -(double)movement / negative;
        return Math.Max(-1.0, Math.Min(1.0, value));
    }
}

public sealed class Profile
{
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(Profile));
    public List<AxisBinding> Axes = new List<AxisBinding>();

    internal static string FilePath(string port)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "ControllerDrone", port + ".xml");
    }

    internal static Profile Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using (var stream = File.OpenRead(path))
            {
                var profile = (Profile)Serializer.Deserialize(stream);
                // Legacy profiles may contain a fifth axis for the gimbal.
                if (profile.Axes != null && profile.Axes.Count == 5)
                    profile.Axes.RemoveAt(4);
                return profile.IsValid() ? profile : null;
            }
        }
        catch (InvalidOperationException) { return null; }
    }

    private bool IsValid()
    {
        if (Axes == null || Axes.Count != 4)
            return false;
        var seen = new HashSet<int>();
        foreach (var axis in Axes)
        {
            if (axis == null || Array.IndexOf(MavicSerial.AxisOffsets, axis.Offset) < 0 ||
                !seen.Add(axis.Offset) || axis.Center < 0 || axis.Center > ushort.MaxValue ||
                axis.Positive < 0 || axis.Positive > ushort.MaxValue ||
                axis.Negative < 0 || axis.Negative > ushort.MaxValue ||
                Math.Abs(axis.Positive - axis.Center) < 100 ||
                Math.Abs(axis.Negative - axis.Center) < 100 ||
                Math.Sign(axis.Positive - axis.Center) == Math.Sign(axis.Negative - axis.Center))
                return false;
        }
        return true;
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary)) Serializer.Serialize(stream, this);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
