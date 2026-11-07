using System;
using System.Diagnostics;
using System.IO.Ports;

internal sealed class MavicSerial : IDisposable
{
    internal const int PayloadStart = 11;
    internal const int PayloadEnd = 36;
    internal static readonly int[] AxisOffsets = { 13, 16, 19, 22, 25, 28, 31, 34 };
    internal readonly byte[] Frame = new byte[38];
    private readonly byte[] request = {
        0x55, 0x0D, 0x04, 0x33, 0x0A, 0x0E, 0x03, 0x00, 0x40, 0x06, 0x01, 0, 0
    };
    private readonly SerialPort port;
    private ushort sequence = 2;

    internal MavicSerial(string name)
    {
        port = new SerialPort(name, 115200, Parity.None, 8, StopBits.One) {
            ReadTimeout = 20, WriteTimeout = 700
        };
        try { port.Open(); }
        catch { port.Dispose(); throw; }
    }

    internal bool Read()
    {
        unchecked { sequence++; }
        PrepareRequest(request, sequence);
        var timer = Stopwatch.StartNew();
        port.Write(request, 0, request.Length);

        int position = 0;
        while (timer.ElapsedMilliseconds < 180)
        {
            int value;
            try { value = port.ReadByte(); }
            catch (TimeoutException) { continue; }

            if (position == 0 && value != 0x55) continue;
            if (position == 1 && value != 0x26)
            {
                position = value == 0x55 ? 1 : 0;
                continue;
            }

            Frame[position++] = (byte)value;
            if (position != Frame.Length) continue;
            if (IsReply(Frame, sequence)) return true;
            position = 0;
        }
        return false;
    }

    internal static void PrepareRequest(byte[] bytes, ushort seq)
    {
        bytes[6] = (byte)seq;
        bytes[7] = (byte)(seq >> 8);
        ushort crc = Checksum(bytes, bytes.Length - 2);
        bytes[11] = (byte)crc;
        bytes[12] = (byte)(crc >> 8);
    }

    internal static bool IsReply(byte[] frame, ushort seq)
    {
        return frame.Length == 38 && frame[0] == 0x55 && frame[1] == 0x26 &&
            Value(frame, 6) == seq && frame[8] == 0x80 && frame[9] == 0x06 &&
            Checksum(frame, PayloadEnd) == Value(frame, PayloadEnd);
    }

    internal static int Value(byte[] bytes, int offset)
    {
        return bytes[offset] | (bytes[offset + 1] << 8);
    }

    private static ushort Checksum(byte[] bytes, int length)
    {
        int crc = 0x3692;
        for (int i = 0; i < length; i++)
        {
            crc ^= bytes[i];
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0x8408);
        }
        return (ushort)crc;
    }

    public void Dispose() { port.Dispose(); }
}
