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

public sealed class ButtonBinding
{
    public byte[] Mask = new byte[MavicSerial.PayloadEnd - MavicSerial.PayloadStart];
    public byte[] Pressed = new byte[MavicSerial.PayloadEnd - MavicSerial.PayloadStart];

    internal bool IsPressed(byte[] frame)
    {
        bool mapped = false;
        for (int i = 0; i < Mask.Length; i++)
        {
            if ((frame[MavicSerial.PayloadStart + i] & Mask[i]) != Pressed[i]) return false;
            mapped |= Mask[i] != 0;
        }
        return mapped;
    }

    internal static ButtonBinding Learn(byte[] before, byte[] held, byte[] heldAgain,
                                        byte[] released, IList<AxisBinding> axes,
                                        IList<ButtonBinding> existing)
    {
        var binding = new ButtonBinding();
        for (int i = MavicSerial.PayloadStart; i < MavicSerial.PayloadEnd; i++)
        {
            bool axisByte = false;
            foreach (var axis in axes)
                if (i == axis.Offset || i == axis.Offset + 1) axisByte = true;
            if (axisByte) continue;

            int index = i - MavicSerial.PayloadStart;
            int mask = (before[i] ^ held[i]) & ~(held[i] ^ heldAgain[i]) & ~(before[i] ^ released[i]);
            foreach (var prior in existing) mask &= ~prior.Mask[index];
            binding.Mask[index] = (byte)mask;
            binding.Pressed[index] = (byte)(held[i] & mask);
        }
        return binding;
    }
}

public sealed class Profile
{
    private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(Profile));
    public List<AxisBinding> Axes = new List<AxisBinding>();
    public List<ButtonBinding> Buttons = new List<ButtonBinding>();

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
                return profile.IsValid() ? profile : null;
            }
        }
        catch (InvalidOperationException) { return null; }
    }

    private bool IsValid()
    {
        if (Axes == null || Buttons == null || Axes.Count < 4 || Axes.Count > 5 || Buttons.Count != 3)
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
        int length = MavicSerial.PayloadEnd - MavicSerial.PayloadStart;
        foreach (var button in Buttons)
        {
            if (button == null || button.Mask == null || button.Pressed == null ||
                button.Mask.Length != length || button.Pressed.Length != length)
                return false;
            for (int i = 0; i < length; i++)
                if ((button.Pressed[i] & ~button.Mask[i]) != 0) return false;
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
