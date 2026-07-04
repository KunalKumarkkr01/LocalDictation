namespace LocalDictation.Shared;

/// <summary>
/// A lightweight success/failure result used on the dictation hot path where failure
/// is expected (no mic, LLM offline, insertion blocked) rather than exceptional.
/// </summary>
/// <remarks>
/// Keeps control flow explicit and testable. Only truly unexpected faults throw.
/// </remarks>
public readonly struct Result
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Human-readable error when <see cref="IsSuccess"/> is false.</summary>
    public string Error { get; }

    private Result(bool success, string error)
    {
        IsSuccess = success;
        Error = error;
    }

    /// <summary>Creates a successful result.</summary>
    public static Result Ok() => new(true, string.Empty);

    /// <summary>Creates a failed result with a message.</summary>
    public static Result Fail(string error) => new(false, error);

    /// <summary>Wraps a value in a successful <see cref="Result{T}"/>.</summary>
    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);

    /// <summary>Creates a failed <see cref="Result{T}"/>.</summary>
    public static Result<T> Fail<T>(string error) => Result<T>.Fail(error);
}

/// <summary>A success/failure result carrying a value on success.</summary>
/// <typeparam name="T">The success value type.</typeparam>
public readonly struct Result<T>
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>The value when successful (default otherwise).</summary>
    public T? Value { get; }

    /// <summary>Human-readable error when failed.</summary>
    public string Error { get; }

    private Result(bool success, T? value, string error)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
    }

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static Result<T> Ok(T value) => new(true, value, string.Empty);

    /// <summary>Creates a failed result with a message.</summary>
    public static Result<T> Fail(string error) => new(false, default, error);

    /// <summary>Returns the value on success or <paramref name="fallback"/> on failure.</summary>
    public T ValueOr(T fallback) => IsSuccess ? Value! : fallback;
}
