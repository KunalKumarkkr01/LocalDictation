using System.Runtime.CompilerServices;

namespace LocalDictation.Shared;

/// <summary>Guard clauses for validating arguments with concise call sites.</summary>
public static class Guard
{
    /// <summary>Throws if <paramref name="value"/> is null.</summary>
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class
        => value ?? throw new ArgumentNullException(name);

    /// <summary>Throws if <paramref name="value"/> is null or whitespace.</summary>
    public static string NotNullOrWhiteSpace(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be null or whitespace.", name)
            : value;
}
