using System.IO.Compression;
using System.Net.Http;
using System.Text;

// These isolated shims model the verified SPT API without initializing Unity, a session, or a network listener.
namespace SPT.Common.Http
{
    public static class RequestHandler
    {
        public static string Host;
        public const string SessionId = "test-session-token";
        public static Client HttpClient;
    }

    public sealed class Client
    {
        public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond;
        private readonly string _address;
        private readonly string _accountId;
        public HttpClient HttpClient { get; } = new HttpClient(new Handler());
        public Client(string address, string accountId, int retries = 3)
        {
            _address = address;
            _accountId = accountId;
        }

        public HttpRequestMessage CreateNewHttpRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, _address + path);
            request.Headers.Add("Cookie", "PHPSESSID=" + _accountId);
            return request;
        }

        private sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
                => Respond(request, token);
        }
    }
}

namespace SPT.Common.Utils
{
    public enum ZlibCompression { Maximum = 9 }
    public static class Zlib
    {
        public static bool IsCompressed(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 120;
        public static byte[] Compress(byte[] bytes, ZlibCompression level)
        {
            using var output = new MemoryStream();
            using (var stream = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                stream.Write(bytes);
            return output.ToArray();
        }
        public static byte[] Decompress(byte[] bytes)
        {
            using var input = new MemoryStream(bytes);
            using var stream = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }
    }
}

namespace UnityEngine
{
    public sealed class WaitForSecondsRealtime(float seconds)
    {
        public float Seconds { get; } = seconds;
    }
}

namespace EFT
{
    public enum WildSpawnType
    {
        assault = 1,
        botgen_boss = 970000,
        botgen_follower = 970001
    }
}

namespace MapLootEditorLite.Client
{
    internal sealed class Setting { public string Value = ""; }
    internal sealed class TestLog
    {
        public readonly List<string> Lines = new List<string>();
        public void LogInfo(object value) => Lines.Add(value.ToString());
        public void LogWarning(object value) => Lines.Add(value.ToString());
    }
    internal static class Plugin
    {
        public static readonly Setting BotGenBackendUrl = new Setting();
        public static readonly TestLog Log = new TestLog();
    }
}
