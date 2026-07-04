namespace LocalDictation.Domain;

/// <summary>The kind of AI post-processing applied to a raw transcript.</summary>
public enum ProcessingMode
{
    /// <summary>Insert the raw transcript verbatim (no LLM call).</summary>
    None = 0,
    /// <summary>Fix grammar, spelling and punctuation without changing meaning.</summary>
    GrammarCorrection,
    /// <summary>Rewrite the text in a clear, professional tone.</summary>
    ProfessionalRewrite,
    /// <summary>Translate the text into the configured target language.</summary>
    Translate,
    /// <summary>Condense the text into a short summary.</summary>
    Summarize,
    /// <summary>Format the text as clean Markdown.</summary>
    MarkdownFormat,
    /// <summary>Apply a user-supplied custom prompt template.</summary>
    Custom
}

/// <summary>Classification of the control that currently holds keyboard focus.</summary>
public enum ControlKind
{
    /// <summary>Could not be determined.</summary>
    Unknown = 0,
    /// <summary>A plain single/multi-line editable text box.</summary>
    EditableTextBox,
    /// <summary>A rich text editor (Word, Outlook, Notion, etc.).</summary>
    RichTextEditor,
    /// <summary>A browser text field / textarea exposed via the a11y tree.</summary>
    BrowserTextArea,
    /// <summary>A console / terminal window (paste-only insertion).</summary>
    Terminal,
    /// <summary>A password or otherwise sensitive field — dictation is blocked.</summary>
    Sensitive,
    /// <summary>A focusable control that does not accept text insertion.</summary>
    Unsupported
}

/// <summary>Lifecycle state of a single dictation session.</summary>
public enum SessionState
{
    Idle = 0,
    Recording,
    Transcribing,
    Processing,
    Delivering,
    Completed,
    Cancelled,
    Failed
}

/// <summary>Available Whisper model sizes ordered by accuracy/cost.</summary>
public enum SpeechModelSize
{
    Tiny = 0,
    Base,
    Small,
    Medium,
    LargeV3
}
