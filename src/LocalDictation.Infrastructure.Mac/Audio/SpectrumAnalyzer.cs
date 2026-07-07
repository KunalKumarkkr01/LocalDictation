namespace LocalDictation.Infrastructure.Mac.Audio;

/// <summary>
/// Computes an N-band frequency magnitude spectrum from a mono PCM window, mirroring the Windows
/// capture service's 13-band reactive meter without depending on a platform FFT (Accelerate/vDSP).
/// </summary>
/// <remarks>
/// Uses a self-contained iterative radix-2 Cooley–Tukey FFT over a Hann-windowed, zero-padded frame,
/// then buckets the lower half of the magnitude spectrum into <see cref="Bands"/> log-ish groups and
/// normalises each to 0..1. Pure managed code — portable, allocation-light, good enough for a UI meter.
/// </remarks>
public sealed class SpectrumAnalyzer
{
    /// <summary>Number of output bands (matches the on-screen waveform bar count).</summary>
    public const int Bands = 13;

    private const int FftSize = 512; // power of two; ~32 ms at 16 kHz

    /// <summary>Produces <see cref="Bands"/> magnitudes (0..1) for the given mono samples.</summary>
    public float[] Compute(ReadOnlySpan<float> samples)
    {
        var re = new double[FftSize];
        var im = new double[FftSize];
        int n = Math.Min(samples.Length, FftSize);
        for (int i = 0; i < n; i++)
        {
            // Hann window to reduce spectral leakage.
            double w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
            re[i] = samples[i] * w;
        }

        Fft(re, im);

        var bands = new float[Bands];
        int half = FftSize / 2;
        int perBand = Math.Max(1, half / Bands);
        for (int b = 0; b < Bands; b++)
        {
            double sum = 0;
            int start = b * perBand;
            int end = Math.Min(start + perBand, half);
            for (int k = start; k < end; k++)
                sum += Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
            double avg = end > start ? sum / (end - start) : 0;
            // Compress and clamp for a lively but bounded meter.
            bands[b] = (float)Math.Clamp(Math.Sqrt(avg) * 2.5, 0, 1);
        }
        return bands;
    }

    /// <summary>In-place iterative radix-2 FFT (length must be a power of two).</summary>
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wRe = Math.Cos(ang), wIm = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tRe = re[b] * curRe - im[b] * curIm;
                    double tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                    re[a] += tRe; im[a] += tIm;
                    double nRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nRe;
                }
            }
        }
    }
}
