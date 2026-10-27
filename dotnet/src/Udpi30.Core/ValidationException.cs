namespace Udpi30.Core;

/// <summary>
/// Raised when a deployment definition fails validation. Mirrors the
/// <c>ValueError</c>s raised by the Pydantic <c>model_validator</c> blocks —
/// the <see cref="Exception.Message"/> carries the same human-readable text.
/// </summary>
public sealed class ValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ValidationException(string message) : base(message)
    {
        Errors = [message];
    }

    public ValidationException(string header, IReadOnlyList<string> errors)
        : base(header + "\n  " + string.Join("\n  ", errors))
    {
        Errors = errors;
    }
}
