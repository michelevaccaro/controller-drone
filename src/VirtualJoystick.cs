using System;
using System.IO;
using System.Runtime.InteropServices;

internal sealed class VirtualJoystick : IDisposable
{
    private const uint Device = 1;
    private const string Library = "vJoyInterface.dll";
    private static readonly uint[] Usages = { 0x30, 0x31, 0x33, 0x34, 0x32 };

    [StructLayout(LayoutKind.Sequential)]
    internal struct Report
    {
        internal byte Device;
        internal int Throttle, Rudder, Aileron, X, Y, Z, Rx, Ry, Rz, Slider, Dial, Wheel;
        internal int Vx, Vy, Vz, Vbrx, Vbry, Vbrz;
        internal uint Buttons, Hat1, Hat2, Hat3, Hat4, Buttons2, Buttons3, Buttons4;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool AcquireVJD(uint id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern void RelinquishVJD(uint id);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool UpdateVJD(uint id, ref Report report);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool GetVJDAxisExist(uint id, uint usage);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool GetVJDAxisMin(uint id, uint usage, out int value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool GetVJDAxisMax(uint id, uint usage, out int value);

    private readonly int[] minimum = new int[Usages.Length];
    private readonly int[] maximum = new int[Usages.Length];
    private readonly Report neutral;
    private readonly bool hasGimbalAxis;

    internal VirtualJoystick()
    {
        LoadInstalledLibrary();
        if (!AcquireVJD(Device))
            throw new InvalidOperationException("vJoy Device 1 occupato o disabilitato. Chiudi il vecchio ponte e vJoy Feeder.");
        try
        {
            for (int i = 0; i < 4; i++)
                if (!GetVJDAxisMin(Device, Usages[i], out minimum[i]) ||
                    !GetVJDAxisMax(Device, Usages[i], out maximum[i]) || maximum[i] <= minimum[i])
                    throw new InvalidOperationException("Abilita X, Y, Rx e Ry in Configure vJoy per Device 1.");

            const int gimbal = 4;
            hasGimbalAxis = GetVJDAxisExist(Device, Usages[gimbal]) &&
                GetVJDAxisMin(Device, Usages[gimbal], out minimum[gimbal]) &&
                GetVJDAxisMax(Device, Usages[gimbal], out maximum[gimbal]) &&
                maximum[gimbal] > minimum[gimbal];
            if (!hasGimbalAxis)
                Console.WriteLine("Per usare la rotellina, abilita Z in Configure vJoy per Device 1 e riavvia Controller Drone.");

            neutral = new Report {
                Device = (byte)Device,
                X = Scale(0, 0), Y = Scale(0, 1), Rx = Scale(0, 2), Ry = Scale(0, 3), Z = Scale(0, hasGimbalAxis ? gimbal : 0),
                Rz = Scale(0, 0), Slider = Scale(0, 0), Dial = Scale(0, 0), Wheel = Scale(0, 0),
                Hat1 = uint.MaxValue, Hat2 = uint.MaxValue, Hat3 = uint.MaxValue, Hat4 = uint.MaxValue
            };
        }
        catch { RelinquishVJD(Device); throw; }
    }

    private static void LoadInstalledLibrary()
    {
        foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            string folder = Path.Combine(root, "vJoy");
            if (!Directory.Exists(folder)) continue;
            foreach (string path in Directory.EnumerateFiles(folder, Library, SearchOption.AllDirectories))
            {
                byte[] header = File.ReadAllBytes(path);
                if (header.Length < 64) continue;
                int pe = BitConverter.ToInt32(header, 60);
                if (pe < 0 || pe > header.Length - 6 ||
                    BitConverter.ToUInt16(header, pe + 4) != 0x8664) continue;
                if (LoadLibrary(path) != IntPtr.Zero) return;
            }
        }
        throw new InvalidOperationException("Impossibile caricare vJoyInterface.dll x64. Verifica l'installazione di vJoy 2.2.2.");
    }

    private int Scale(double value, int axis)
    {
        return (int)Math.Round(minimum[axis] + (value + 1) * 0.5 * (maximum[axis] - minimum[axis]));
    }

    internal void Send(Profile profile, byte[] frame)
    {
        Report report = neutral;
        if (frame != null)
        {
            report.X = Scale(profile.Axes[0].Read(frame), 0);
            report.Y = Scale(profile.Axes[1].Read(frame), 1);
            report.Rx = Scale(profile.Axes[2].Read(frame), 2);
            report.Ry = Scale(profile.Axes[3].Read(frame), 3);
            if (hasGimbalAxis) report.Z = Scale(MavicSerial.ReadGimbal(frame), 4);
        }
        if (!UpdateVJD(Device, ref report))
            throw new InvalidOperationException("vJoy non ha accettato il report.");
    }

    public void Dispose()
    {
        Report report = neutral;
        try { UpdateVJD(Device, ref report); }
        finally { RelinquishVJD(Device); }
    }
}
