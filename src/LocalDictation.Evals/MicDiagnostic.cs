using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LocalDictation.Evals;

/// <summary>
/// Diagnoses microphone capture: which devices exist, which is the Windows default, and whether
/// each records any signal. Run with <c>dotnet run -- mic</c>. Speak during the countdown to see
/// live levels. Used to confirm the app records from the right device (root-cause evidence).
/// </summary>
public static class MicDiagnostic
{
    /// <summary>Runs the capture diagnostic and prints device + level evidence.</summary>
    public static void Run()
    {
        Console.WriteLine("=== WaveIn (legacy) devices ===");
        Console.WriteLine($"  WAVE_MAPPER (-1) = system default input");
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            Console.WriteLine($"  [{i}] {WaveInEvent.GetCapabilities(i).ProductName}");

        Console.WriteLine("\n=== WASAPI capture endpoints ===");
        try
        {
            using var en = new MMDeviceEnumerator();
            var def = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                Console.WriteLine($"  {(d.ID == def.ID ? "* " : "  ")}{d.FriendlyName}");
            Console.WriteLine($"  (default = {def.FriendlyName})");
        }
        catch (Exception ex) { Console.WriteLine("  WASAPI enumeration failed: " + ex.Message); }

        RecordAndReport("WaveIn device 0 (current app default)", 0);
        RecordAndReport("WAVE_MAPPER -1 (Windows default input)", -1);
    }

    private static void RecordAndReport(string label, int device)
    {
        Console.WriteLine($"\n--- Recording 4s from {label} — SPEAK NOW ---");
        double maxRms = 0, maxPeak = 0;
        long samples = 0;
        try
        {
            using var waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                DeviceNumber = device,
                BufferMilliseconds = 100
            };
            waveIn.DataAvailable += (_, e) =>
            {
                double sumSq = 0; double peak = 0;
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    short s = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                    float f = s / 32768f;
                    sumSq += f * f;
                    if (Math.Abs(f) > peak) peak = Math.Abs(f);
                }
                int n = e.BytesRecorded / 2;
                samples += n;
                double rms = n > 0 ? Math.Sqrt(sumSq / n) : 0;
                if (rms > maxRms) maxRms = rms;
                if (peak > maxPeak) maxPeak = peak;
                Console.Write($"\r  level: rms={rms:F4} peak={peak:F3}   ");
            };
            waveIn.StartRecording();
            Thread.Sleep(4000);
            waveIn.StopRecording();
            Thread.Sleep(200);
            Console.WriteLine($"\n  RESULT: {samples} samples ({samples / 16000.0:F1}s), maxRms={maxRms:F4}, maxPeak={maxPeak:F3}");
            Console.WriteLine($"  Verdict: {(maxRms > 0.01 ? "SIGNAL DETECTED" : maxRms > 0.001 ? "faint/no speech" : "SILENT (no capture?)")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  FAILED to open device {device}: {ex.Message}");
        }
    }
}
