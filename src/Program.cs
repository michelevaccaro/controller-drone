using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

internal static class Program
{
    private static volatile bool stopping;

    private static int Main(string[] args)
    {
        Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) {
            e.Cancel = true;
            stopping = true;
        };
        try
        {
            string portName = "COM5";
            bool recalibrate = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--self-test": SelfTest.Run(); return 0;
                    case "--help":
                        Console.WriteLine("ControllerDrone.exe [--port COM5] [--recalibrate] [--self-test]");
                        return 0;
                    case "--recalibrate": recalibrate = true; break;
                    case "--port":
                        if (++i >= args.Length || !Regex.IsMatch(args[i], @"\ACOM[1-9][0-9]*\z", RegexOptions.IgnoreCase))
                            throw new ArgumentException("Indica una porta come --port COM5.");
                        portName = args[i].ToUpperInvariant();
                        break;
                    default: throw new ArgumentException("Opzione sconosciuta: " + args[i]);
                }
            }

            Console.WriteLine("Controller Drone | " + portName + " -> vJoy Device 1");
            using (var radio = new MavicSerial(portName))
            using (var joystick = new VirtualJoystick())
            {
                string path = Profile.FilePath(portName);
                var profile = recalibrate ? null : Profile.Load(path);
                if (profile == null)
                {
                    profile = Calibrate(radio);
                    profile.Save(path);
                    Console.WriteLine("Calibrazione salvata.");
                }
                else Console.WriteLine("Calibrazione caricata. Usa --recalibrate per rifarla.");

                Console.WriteLine("Pronto. Apri il simulatore o Monitor vJoy. Ctrl+C per uscire.");
                Run(radio, joystick, profile);
            }
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception error)
        {
            Console.Error.WriteLine("ERRORE: " + error.Message);
            if (args.Length == 0 && !Console.IsInputRedirected)
            {
                Console.WriteLine("Premi Invio per chiudere.");
                Console.ReadLine();
            }
            return 1;
        }
    }

    private static bool Ask(string message, bool canSkip)
    {
        Console.WriteLine(message + (canSkip ? " Premi Invio oppure S + Invio per saltare." : " Premi Invio."));
        string answer = Console.ReadLine();
        if (stopping || answer == null) throw new OperationCanceledException();
        return !canSkip || !string.Equals(answer.Trim(), "s", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Sample(MavicSerial radio)
    {
        for (int attempt = 0; attempt < 5 && !stopping; attempt++)
            if (radio.Read()) return (byte[])radio.Frame.Clone();
        if (stopping) throw new OperationCanceledException();
        throw new InvalidOperationException("Il radiocomando non risponde. Controlla porta USB e alimentazione.");
    }

    private static Profile Calibrate(MavicSerial radio)
    {
        var profile = new Profile();
        Ask("Lascia gli stick e la rotellina al centro.", false);
        byte[] center = Sample(radio);
        string[] controls = { "stick sinistro orizzontale", "stick sinistro verticale",
                              "stick destro orizzontale", "stick destro verticale", "rotellina del gimbal" };
        string[] positive = { "destra", "in basso", "destra", "in basso", "destra" };
        string[] negative = { "sinistra", "in alto", "sinistra", "in alto", "sinistra" };
        var used = new HashSet<int>();
        for (int i = 0; i < controls.Length; i++)
        {
            while (Ask("Tieni " + controls[i] + " tutto " + positive[i] + ". Muovi solo questo comando.", i == 4))
            {
                byte[] high = Sample(radio);
                Ask("Ora tieni " + controls[i] + " tutto " + negative[i] + ".", false);
                byte[] low = Sample(radio);
                AxisBinding axis = LearnAxis(center, high, low, used);
                if (axis == null)
                {
                    Console.WriteLine("Asse non distinto. Ripeti mantenendo gli altri comandi al centro.");
                    continue;
                }
                profile.Axes.Add(axis);
                used.Add(axis.Offset);
                break;
            }
        }

        foreach (string name in new[] { "Video", "Foto", "Pausa/RTH" })
        {
            var binding = new ButtonBinding();
            while (true)
            {
                if (!Ask("Rilascia i pulsanti per calibrare " + name + ".", true)) break;
                byte[] before = Sample(radio);
                Ask("Tieni premuto " + name + " mentre premi Invio.", false);
                byte[] held = Sample(radio);
                Thread.Sleep(40);
                byte[] heldAgain = Sample(radio);
                Ask("Rilascia " + name + ".", false);
                byte[] released = Sample(radio);
                binding = ButtonBinding.Learn(before, held, heldAgain, released, profile.Axes, profile.Buttons);
                if (binding.IsPressed(held) && !binding.IsPressed(released)) break;
                Console.WriteLine("Pulsante non rilevato. Puoi riprovare oppure saltarlo.");
            }
            profile.Buttons.Add(binding);
        }
        return profile;
    }

    internal static AxisBinding LearnAxis(byte[] center, byte[] positive, byte[] negative, ISet<int> used)
    {
        AxisBinding best = null;
        int strength = 99;
        foreach (int offset in MavicSerial.AxisOffsets)
        {
            if (used.Contains(offset)) continue;
            int middle = MavicSerial.Value(center, offset);
            int high = MavicSerial.Value(positive, offset);
            int low = MavicSerial.Value(negative, offset);
            int movement = Math.Min(Math.Abs(high - middle), Math.Abs(low - middle));
            if (movement <= strength || Math.Sign(high - middle) == Math.Sign(low - middle)) continue;
            best = new AxisBinding { Offset = offset, Center = middle, Positive = high, Negative = low };
            strength = movement;
        }
        return best;
    }

    private static void Run(MavicSerial radio, VirtualJoystick joystick, Profile profile)
    {
        var clock = Stopwatch.StartNew();
        double windowStart = 0, lastReport = 0, totalLatency = 0, maxGap = 0;
        int sent = 0, missed = 0;
        bool hasReport = false, neutral = true;
        while (!stopping)
        {
            double start = clock.Elapsed.TotalMilliseconds;
            if (radio.Read())
            {
                double received = clock.Elapsed.TotalMilliseconds;
                joystick.Send(profile, radio.Frame);
                double now = clock.Elapsed.TotalMilliseconds;
                if (hasReport) maxGap = Math.Max(maxGap, now - lastReport);
                lastReport = now;
                totalLatency += received - start;
                sent++;
                hasReport = true;
                neutral = false;
            }
            else
            {
                missed++;
                if (!neutral && clock.Elapsed.TotalMilliseconds - lastReport >= 500)
                {
                    joystick.Send(profile, null);
                    neutral = true;
                    Console.WriteLine("Risposte sospese. Assi al centro, attendo il radiocomando.");
                }
            }

            double elapsed = clock.Elapsed.TotalMilliseconds;
            if (elapsed - windowStart >= 3000)
            {
                double rate = sent * 1000.0 / (elapsed - windowStart);
                string latency = sent == 0 ? "-" : (totalLatency / sent).ToString("F1");
                Console.WriteLine("vJoy: {0:F1} aggiornamenti/s | mancate: {1} | risposta media: {2} ms | intervallo max: {3:F1} ms",
                                  rate, missed, latency, maxGap);
                windowStart = elapsed;
                sent = missed = 0;
                totalLatency = maxGap = 0;
            }
            double wait = 10 - (clock.Elapsed.TotalMilliseconds - start);
            if (wait > 0) Thread.Sleep((int)Math.Ceiling(wait));
        }
    }
}
