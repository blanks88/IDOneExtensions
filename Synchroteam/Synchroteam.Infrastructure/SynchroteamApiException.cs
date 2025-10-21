using System.Net;
using System.Text.Json;

namespace Synchroteam.Infrastructure;

public sealed class SynchroteamApiException(
    HttpStatusCode statusCode,
    string message,
    string? responseBody = null,
    string? errorCode = null,
    string? requestId = null,
    Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ResponseBody { get; } = responseBody;
    public string? ErrorCode { get; } = errorCode;
    public string? RequestId { get; } = requestId;

    public static SynchroteamApiException FromResponse(HttpStatusCode statusCode, string? body, string? requestId = null)
    {
        var message = $"Synchroteam.Infrastructure API responded with status {(int)statusCode} ({statusCode}).";
        string? errorCode = null;
        try
        {
            using var doc = JsonDocument.Parse(body ?? "{}");
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
            {
                message = msgProp.GetString() ?? message;
            }
            if (root.TryGetProperty("error", out var errProp) && errProp.ValueKind == JsonValueKind.String)
            {
                errorCode = errProp.GetString();
            }
        }
        catch
        {
            // ignore parse errors
        }
        return new SynchroteamApiException(statusCode, message, body, errorCode, requestId);
    }
}
