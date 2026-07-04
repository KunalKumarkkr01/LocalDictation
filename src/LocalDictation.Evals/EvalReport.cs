namespace LocalDictation.Evals;

/// <summary>Top-level evaluation report serialised to <c>artifacts/eval-report.json</c>.</summary>
public sealed class EvalReport
{
    /// <summary>Logical processor count of the eval machine.</summary>
    public int Cores { get; set; }
    /// <summary>Per-model ASR results.</summary>
    public List<ModelResult> Asr { get; set; } = new();
    /// <summary>Whether the local LLM was reachable during the run.</summary>
    public bool LlmAvailable { get; set; }
    /// <summary>Configured LLM model tag.</summary>
    public string LlmModel { get; set; } = "";
    /// <summary>LLM post-processing samples.</summary>
    public List<LlmResult> Llm { get; set; } = new();
}

/// <summary>Aggregated ASR results for one Whisper model.</summary>
public sealed class ModelResult
{
    /// <summary>Model name.</summary>
    public string Model { get; set; } = "";
    /// <summary>Mean word error rate across fixtures.</summary>
    public double AvgWer { get; set; }
    /// <summary>Mean real-time factor.</summary>
    public double AvgRtf { get; set; }
    /// <summary>Mean transcription time in ms.</summary>
    public double AvgMs { get; set; }
    /// <summary>Per-fixture detail.</summary>
    public List<FixtureResult> Fixtures { get; set; } = new();
}

/// <summary>ASR result for a single fixture.</summary>
public sealed class FixtureResult
{
    /// <summary>Fixture id.</summary>
    public string Id { get; set; } = "";
    /// <summary>Ground-truth text.</summary>
    public string Reference { get; set; } = "";
    /// <summary>Whisper output.</summary>
    public string Hypothesis { get; set; } = "";
    /// <summary>Word error rate (0..1).</summary>
    public double Wer { get; set; }
    /// <summary>Transcription time in ms.</summary>
    public double Ms { get; set; }
    /// <summary>Real-time factor.</summary>
    public double Rtf { get; set; }
}

/// <summary>One LLM post-processing sample.</summary>
public sealed class LlmResult
{
    /// <summary>Processing mode.</summary>
    public string Mode { get; set; } = "";
    /// <summary>Input text.</summary>
    public string Input { get; set; } = "";
    /// <summary>LLM output.</summary>
    public string Output { get; set; } = "";
    /// <summary>Latency in ms.</summary>
    public long Ms { get; set; }
}
