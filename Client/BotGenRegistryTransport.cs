using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SPT.Common.Http;
using SPT.Common.Utils;

namespace MapLootEditorLite.Client
{
    internal sealed class BotGenRegistryResponse
    {
        public string Json;
        public string Failure;
        public string Context;
    }

    internal static class BotGenRegistryTransport
    {
        internal const string Route = "/botgen/registry";
        internal const string LegacyDefault = "http://127.0.0.1:6969/botgen/registry";
        internal const int TimeoutSeconds = 12;

        internal static bool TryResolveBackend(string value, out string backend, out string error)
        {
            backend = null;
            error = null;
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                error = "expected an absolute HTTP(S) backend URL without credentials, query, or fragment";
                return false;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.EndsWith(Route, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - Route.Length).TrimEnd('/');
            if (path.IndexOf(Route, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                error = "backend URL contains a repeated registry route";
                return false;
            }
            backend = uri.GetLeftPart(UriPartial.Authority) + path;
            return true;
        }

        internal static string MigrateLegacyUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim().TrimEnd('/'), LegacyDefault, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return TryResolveBackend(value, out var backend, out _) ? backend : value;
        }

        internal static async Task<BotGenRegistryResponse> FetchAsync(string backendOverride)
        {
            var watch = Stopwatch.StartNew();
            var automatic = string.IsNullOrWhiteSpace(backendOverride);
            var mode = automatic ? "automatic SPT discovery" : "manual override";
            var backend = "(unavailable)";
            int? status = null;
            SPT.Common.Http.Client ownedClient = null;
            try
            {
                // Validate an override before initializing SPT or forwarding its session.
                var configured = backendOverride;
                if (automatic)
                    configured = RequestHandler.Host;
                if (automatic && string.IsNullOrWhiteSpace(configured))
                    return Failure("SPT backend address is not available", "backend-unavailable");
                if (!TryResolveBackend(configured, out var resolved, out var error))
                    return Failure("Backend configuration invalid: " + error, "configuration-error");
                backend = resolved;

                var client = RequestHandler.HttpClient;
                if (!automatic)
                {
                    ownedClient = new SPT.Common.Http.Client(backend, RequestHandler.SessionId, retries: 0);
                    client = ownedClient;
                }

                // Use SPT's request/session factory, HTTP client and framing, but not its immediate retry loop.
                using (var request = client.CreateNewHttpRequest(HttpMethod.Post, Route))
                using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds)))
                {
                    request.RequestUri = new Uri(backend + Route);
                    request.Content = new ByteArrayContent(Zlib.Compress(Encoding.UTF8.GetBytes("{}"), ZlibCompression.Maximum));
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    Plugin.Log.LogInfo(BotGenNpcProvider.Prefix + " " + Context("sending"));
                    using (var response = await client.HttpClient.SendAsync(request, cancellation.Token).ConfigureAwait(false))
                    {
                        status = (int)response.StatusCode;
                        if (!response.IsSuccessStatusCode)
                            return Failure("HTTP error response " + status.Value, "http-error");
                        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (Zlib.IsCompressed(bytes))
                            bytes = Zlib.Decompress(bytes);
                        return new BotGenRegistryResponse
                        {
                            Json = Encoding.UTF8.GetString(bytes),
                            Context = Context("response-received")
                        };
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is WebException || ex is SocketException
                || ex is OperationCanceledException || ex is IOException || ex is TypeInitializationException
                || ex is InvalidOperationException || ex is ArgumentException)
            {
                var classification = ClassifyFailure(ex);
                return Failure(classification + ": " + DescribeFailure(ex), classification);
            }
            finally
            {
                ownedClient?.HttpClient.Dispose();
            }

            string Context(string result)
            {
                return "POST backend=" + backend + " path=" + Route + " mode=" + mode
                    + " result=" + result + " HTTP=" + (status?.ToString() ?? "unavailable")
                    + " elapsedMs=" + watch.ElapsedMilliseconds;
            }

            BotGenRegistryResponse Failure(string detail, string result)
            {
                return new BotGenRegistryResponse { Failure = detail, Context = Context(result) };
            }
        }

        internal static string ClassifyFailure(Exception error)
        {
            var unavailable = error is TypeInitializationException;
            for (var current = error; current != null; current = current.InnerException)
            {
                if (current is OperationCanceledException)
                    return "timeout";
                if (current is SocketException socket)
                {
                    if (socket.SocketErrorCode == SocketError.ConnectionRefused)
                        return "connection-refused";
                    if (socket.SocketErrorCode == SocketError.TimedOut)
                        return "timeout";
                    unavailable = true;
                }
                if (current is WebException web && web.Status == WebExceptionStatus.Timeout)
                    return "timeout";
                if (current is WebException connection && (connection.Status == WebExceptionStatus.ConnectFailure
                    || connection.Status == WebExceptionStatus.NameResolutionFailure))
                    unavailable = true;
            }
            return unavailable ? "backend-unavailable" : "transport-error";
        }

        private static string DescribeFailure(Exception error)
        {
            var details = new StringBuilder();
            for (var current = error; current != null; current = current.InnerException)
            {
                if (details.Length > 0) details.Append(" -> ");
                details.Append(current.GetType().Name).Append("(HResult=").Append(current.HResult).Append(")");
                if (current is SocketException socket)
                    details.Append(" SocketError=").Append(socket.SocketErrorCode).Append(" NativeError=").Append(socket.NativeErrorCode);
                if (current is WebException web)
                    details.Append(" WebStatus=").Append(web.Status);
            }
            // Exception messages/stacks can include SPT's session-bearing requests; log typed native details instead.
            return details.ToString();
        }
    }
}
