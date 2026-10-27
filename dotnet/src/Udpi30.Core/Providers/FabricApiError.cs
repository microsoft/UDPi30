namespace Udpi30.Core.Providers;

/// <summary>Raised when a Fabric API call fails.</summary>
public sealed class FabricApiError(int statusCode, string message, string? requestId = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? RequestId { get; } = requestId;

    public override string ToString()
    {
        var msg = $"Fabric API Error ({StatusCode}): {Message}";
        if (!string.IsNullOrEmpty(RequestId))
        {
            msg += $" [request_id={RequestId}]";
        }
        return msg;
    }
}
