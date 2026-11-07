using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

internal static class SelfTest
{
    internal static void Run()
    {
        byte[] request = Hex("55 0D 04 33 0A 0E 03 00 40 06 01 00 00");
        MavicSerial.PrepareRequest(request, 3);
        Check(request[11] == 0xF4 && request[12] == 0x4A, "request checksum");
        byte[] center = Hex("55 26 04 D1 0E 0A 03 00 80 06 01 00 00 00 04 00 00 04 00 00 04 00 00 04 00 00 04 00 00 04 00 00 04 00 00 04 26 98");
        byte[] right = Hex("55 26 04 D1 0E 0A 03 00 80 06 01 00 00 00 04 00 00 04 00 00 04 00 94 06 00 00 04 00 00 04 00 00 04 00 00 04 CC 30");
        Check(MavicSerial.IsReply(center, 3) && MavicSerial.IsReply(right, 3), "captured replies");
        Check(!MavicSerial.IsReply(right, 4), "late reply rejected");
        byte[] corrupt = (byte[])right.Clone();
        corrupt[22] ^= 1;
        Check(!MavicSerial.IsReply(corrupt, 3), "invalid checksum rejected");

        byte[] left = (byte[])center.Clone();
        left[22] = 0x6C;
        left[23] = 0x01;
        AxisBinding axis = Program.LearnAxis(center, right, left, new HashSet<int>());
        Check(axis != null && axis.Offset == 22 && axis.Center == 1024, "axis mapping");
        Check(axis.Read(center) == 0 && axis.Read(right) == 1 && axis.Read(left) == -1, "axis normalization");
        Check(Program.LearnAxis(center, right, left, new HashSet<int> { 22 }) == null, "no duplicate axes");
        AxisBinding reverse = Program.LearnAxis(center, left, right, new HashSet<int>());
        Check(reverse.Read(left) == 1 && reverse.Read(right) == -1, "reverse physical orientation");

        byte[] pressed = (byte[])center.Clone();
        pressed[11] ^= 4;
        ButtonBinding button = ButtonBinding.Learn(center, pressed, pressed, center,
                                                   new[] { axis }, new ButtonBinding[0]);
        Check(button.IsPressed(pressed) && !button.IsPressed(center), "button press and release");
        ButtonBinding skipped = ButtonBinding.Learn(center, pressed, pressed, center,
                                                    new[] { axis }, new[] { button });
        Check(!skipped.IsPressed(pressed), "button bits cannot be reused");
        Check(Marshal.SizeOf(typeof(VirtualJoystick.Report)) == 108 &&
              Marshal.OffsetOf(typeof(VirtualJoystick.Report), "X").ToInt32() == 16 &&
              Marshal.OffsetOf(typeof(VirtualJoystick.Report), "Buttons").ToInt32() == 76,
              "vJoy report layout");

        var profile = new Profile();
        foreach (int offset in new[] { 13, 16, 19, 22, 25 })
            profile.Axes.Add(new AxisBinding { Offset = offset, Center = 1024, Positive = 1684, Negative = 364 });
        profile.Buttons.AddRange(new[] { button, skipped, new ButtonBinding() });
        string directory = Path.Combine(Path.GetTempPath(), "ControllerDroneTest-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "test.xml");
        try
        {
            profile.Save(path);
            profile.Save(path);
            Profile saved = Profile.Load(path);
            Check(saved != null && saved.Axes.Count == 5 && saved.Buttons[0].IsPressed(pressed), "profile round trip");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        Console.WriteLine("Self-test OK: protocollo, calibrazione, pulsanti, profilo e formato vJoy.");
    }

    private static byte[] Hex(string hex)
    {
        return Array.ConvertAll(hex.Split(' '), item => Convert.ToByte(item, 16));
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Self-test: " + name);
    }
}
