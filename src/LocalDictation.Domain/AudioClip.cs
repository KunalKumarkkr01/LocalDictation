namespace LocalDictation.Domain;

/// <summary>
/// An immutable captured audio buffer, normalised to the format Whisper expects
/// (16 kHz, mono, 32-bit IEEE float samples in the range -1..1).
/// </summary>
/// <remarks>
/// Produced by the audio capture service and consumed by <c>ISpeechEngine</c>.
/// Held in memory only; never written to disk unless the user enables audio retention.
/// </remarks>
public sealed class AudioClip
{
    /// <summary>The canonical sample rate required by Whisper.</summary>
    public const int RequiredSampleRate = 16_000;

    /// <summary>Mono 16 kHz float samples.</summary>
    public float[] Samples { get; }

    /// <summary>Sample rate of <see cref="Samples"/> (always 16 kHz for a valid clip).</summary>
    public int SampleRate { get; }

    /// <summary>Duration of the captured audio.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);

    /// <summary>Creates a clip from mono float samples.</summary>
    /// <param name="samples">Mono float PCM samples in -1..1.</param>
    /// <param name="sampleRate">Sample rate; must equal 16 kHz for downstream use.</param>
    public AudioClip(float[] samples, int sampleRate = RequiredSampleRate)
    {
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        SampleRate = sampleRate;
    }

    /// <summary>True when the clip carries no audio.</summary>
    public bool IsEmpty => Samples.Length == 0;
}
