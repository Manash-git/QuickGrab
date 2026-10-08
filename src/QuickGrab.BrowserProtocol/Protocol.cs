using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace QuickGrab.BrowserProtocol;

public sealed record BrowserRequest(int Version, string Command, string RequestId,
    string? Url = null, string? SuggestedName = null, long? ExpectedBytes = null,
    string? Mime = null, bool Incognito = false);
public sealed record BrowserReply(bool Ok, string Status, string? JobId = null,
    string? Message = null, string Version = "0.2.0");
public static class Protocol
{
    public const string HostName = "com.quickgrab.bridge";
    public const int MaxBytes = 65536;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static string PipeName => "QuickGrab.Browser." + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..24];
    public static void Validate(BrowserRequest request)
    {
        if (request.Version != 1 || request.Command is not ("ping" or "prepare" or "commit" or "abort"))
            throw new InvalidDataException("Unsupported browser protocol.");
        if (!Guid.TryParseExact(request.RequestId, "D", out _)) throw new InvalidDataException("Invalid request identifier.");
        if (request.Command != "prepare") return;
        if (request.Url is null || request.Url.Length > 16384 ||
            !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Only HTTP/HTTPS links without embedded credentials are supported.");
        if (request.SuggestedName?.Length > 512 || request.Mime?.Length > 200 || request.ExpectedBytes < 0)
            throw new InvalidDataException("Invalid download metadata.");
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxBytes) throw new InvalidDataException("Browser message is too large or empty.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Empty browser message.");
    }
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Browser message is too large.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct);
    }
}
