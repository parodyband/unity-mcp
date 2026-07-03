using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal static class OpenUnityMcpServer
    {
        private const int MaxBodyBytes = 1024 * 1024;

        private static readonly object Gate = new object();
        private static TcpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;
        private static int _port;

        // Auth snapshot captured on the main thread at Start(). The accept loop and
        // its ThreadPool workers must never read EditorPrefs (main-thread-only), so
        // the required flag and token are copied here once per server start. A
        // settings change therefore takes effect on the next server start.
        private static volatile bool _requireToken;
        private static volatile string _accessToken;

        public static bool IsRunning => _running;
        public static int Port => _port;
        public static string Endpoint => "http://127.0.0.1:" + _port + "/mcp";

        public static void Start(int port)
        {
            // Snapshot auth settings on the calling (main) thread before any request
            // is served; EditorPrefs is not safe to read from the accept loop.
            var requireToken = OpenUnityMcpSettings.RequireAccessToken;
            var accessToken = OpenUnityMcpSettings.AccessToken;

            lock (Gate)
            {
                if (_running)
                {
                    return;
                }

                _port = port;
                _requireToken = requireToken;
                _accessToken = accessToken;
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                _running = true;
                _thread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "Open Unity MCP"
                };
                _thread.Start();
            }

            // The token is always written to the status file (whether or not
            // enforcement is on) so the sidecar can attach it unconditionally.
            UnityMcpStatusFile.WriteRunning(port, accessToken);
            Debug.Log("[Open Unity MCP] Listening on " + Endpoint +
                      (requireToken ? " (access token required)" : string.Empty));
        }

        public static void Stop()
        {
            lock (Gate)
            {
                if (!_running)
                {
                    return;
                }

                _running = false;
                try
                {
                    _listener?.Stop();
                }
                catch
                {
                    // Listener shutdown is best-effort during domain reload and editor quit.
                }

                _listener = null;
                _thread = null;
            }

            Debug.Log("[Open Unity MCP] Stopped.");
        }

        private static void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
                catch (SocketException)
                {
                    if (_running)
                    {
                        Debug.LogWarning("[Open Unity MCP] Socket accept failed.");
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private static void HandleClient(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = 30000;
                client.SendTimeout = 30000;

                try
                {
                    var request = ReadHttpRequest(client.GetStream());
                    var response = HandleHttpRequest(request);
                    WriteHttpResponse(client.GetStream(), response);
                }
                catch (Exception ex)
                {
                    try
                    {
                        var error = JsonRpcError(null, -32603, ex.Message);
                        WriteHttpResponse(client.GetStream(), new HttpResponse(500, "application/json", error));
                    }
                    catch
                    {
                        // The client disconnected mid-request; nothing can be written back, and an
                        // exception escaping here would surface as an unhandled ThreadPool error.
                    }
                }
            }
        }

        private static HttpResponse HandleHttpRequest(HttpRequest request)
        {
            if (!IsLocalOrigin(request))
            {
                return new HttpResponse(403, "application/json", JsonRpcError(null, -32000, "Forbidden origin."));
            }

            if (request.Path == "/health")
            {
                return new HttpResponse(200, "application/json", McpJson.Stringify(McpJson.Object(
                    "ok", true,
                    "endpoint", Endpoint,
                    "package", "com.strangeape.open-unity-mcp")));
            }

            if (request.Path != "/mcp")
            {
                return new HttpResponse(404, "text/plain; charset=utf-8", "Not found.");
            }

            // /health stays unauthenticated (liveness probe, no sensitive data); the
            // token gate applies only to /mcp. Enforced only when the user opted in.
            if (_requireToken && !HasValidAccessToken(request))
            {
                return new HttpResponse(401, "application/json", JsonRpcError(null, -32001,
                    "Missing or invalid access token. Open Unity MCP requires an access token for /mcp. " +
                    "Copy the token from Preferences > Open Unity MCP and send it as an " +
                    "'Authorization: Bearer <token>' or 'X-Open-Unity-Mcp-Token: <token>' header."),
                    "WWW-Authenticate: Bearer\r\n");
            }

            if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponse(405, "application/json", JsonRpcError(null, -32601, "SSE is not implemented. Use POST for JSON-RPC requests."), "Allow: POST\r\n");
            }

            if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponse(405, "application/json", JsonRpcError(null, -32601, "Method not allowed."), "Allow: POST\r\n");
            }

            if (request.Body == null || request.Body.Length == 0)
            {
                return new HttpResponse(400, "application/json", JsonRpcError(null, -32700, "Empty JSON-RPC body."));
            }

            var protocolResponse = McpProtocol.Handle(request.Body);
            if (protocolResponse.AcceptedNotification)
            {
                return new HttpResponse(202, "text/plain; charset=utf-8", string.Empty);
            }

            return new HttpResponse(protocolResponse.HttpStatus, "application/json", protocolResponse.Body);
        }

        // True when the request carries the configured access token via either the
        // standard Authorization: Bearer header or the X-Open-Unity-Mcp-Token header
        // (for clients that cannot set Authorization). Ordinal comparison is fine on
        // a loopback-only server. Header keys are stored lowercased by the parser.
        private static bool HasValidAccessToken(HttpRequest request)
        {
            var expected = _accessToken;
            if (string.IsNullOrEmpty(expected))
            {
                // Enforcement is on but no token was snapshotted — fail closed.
                return false;
            }

            if (request.Headers.TryGetValue("authorization", out var authorization) && !string.IsNullOrEmpty(authorization))
            {
                const string bearerPrefix = "Bearer ";
                if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = authorization.Substring(bearerPrefix.Length).Trim();
                    if (string.Equals(candidate, expected, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            if (request.Headers.TryGetValue("x-open-unity-mcp-token", out var headerToken) && !string.IsNullOrEmpty(headerToken))
            {
                if (string.Equals(headerToken.Trim(), expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLocalOrigin(HttpRequest request)
        {
            if (!request.Headers.TryGetValue("origin", out var origin) || string.IsNullOrEmpty(origin))
            {
                return true;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        private static HttpRequest ReadHttpRequest(NetworkStream stream)
        {
            var bytes = new List<byte>(4096);
            var buffer = new byte[1024];
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    throw new IOException("Client disconnected before sending headers.");
                }

                for (var i = 0; i < read; i++)
                {
                    bytes.Add(buffer[i]);
                }

                headerEnd = FindHeaderEnd(bytes);
                if (bytes.Count > MaxBodyBytes)
                {
                    throw new InvalidOperationException("Request is too large.");
                }
            }

            var headerText = Encoding.ASCII.GetString(bytes.GetRange(0, headerEnd).ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
            {
                throw new FormatException("Missing HTTP request line.");
            }

            var parts = lines[0].Split(' ');
            if (parts.Length < 2)
            {
                throw new FormatException("Invalid HTTP request line.");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                headers[line.Substring(0, colon).Trim().ToLowerInvariant()] = line.Substring(colon + 1).Trim();
            }

            var contentLength = 0;
            if (headers.TryGetValue("content-length", out var lengthText))
            {
                if (!int.TryParse(lengthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength) || contentLength < 0)
                {
                    throw new FormatException("Invalid Content-Length header.");
                }
            }

            if (contentLength > MaxBodyBytes)
            {
                throw new InvalidOperationException("Request body is too large.");
            }

            var bodyStart = headerEnd + 4;
            var availableBodyBytes = bytes.Count - bodyStart;
            var body = new byte[contentLength];
            if (availableBodyBytes > 0)
            {
                Array.Copy(bytes.GetRange(bodyStart, Math.Min(availableBodyBytes, contentLength)).ToArray(), body, Math.Min(availableBodyBytes, contentLength));
            }

            var offset = Math.Min(availableBodyBytes, contentLength);
            while (offset < contentLength)
            {
                var read = stream.Read(body, offset, contentLength - offset);
                if (read <= 0)
                {
                    throw new IOException("Client disconnected before sending request body.");
                }

                offset += read;
            }

            return new HttpRequest(parts[0], NormalizePath(parts[1]), headers, Encoding.UTF8.GetString(body));
        }

        private static int FindHeaderEnd(List<byte> bytes)
        {
            for (var i = 3; i < bytes.Count; i++)
            {
                if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
                {
                    return i - 3;
                }
            }

            return -1;
        }

        private static string NormalizePath(string rawPath)
        {
            var question = rawPath.IndexOf('?');
            return question >= 0 ? rawPath.Substring(0, question) : rawPath;
        }

        private static void WriteHttpResponse(NetworkStream stream, HttpResponse response)
        {
            var body = Encoding.UTF8.GetBytes(response.Body ?? string.Empty);
            var header =
                "HTTP/1.1 " + response.StatusCode + " " + ReasonPhrase(response.StatusCode) + "\r\n" +
                "Content-Type: " + response.ContentType + "\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n" +
                response.ExtraHeaders +
                "\r\n";

            var headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        private static string ReasonPhrase(int status)
        {
            switch (status)
            {
                case 200:
                    return "OK";
                case 202:
                    return "Accepted";
                case 400:
                    return "Bad Request";
                case 401:
                    return "Unauthorized";
                case 403:
                    return "Forbidden";
                case 404:
                    return "Not Found";
                case 405:
                    return "Method Not Allowed";
                default:
                    return "Internal Server Error";
            }
        }

        private static string JsonRpcError(object id, int code, string message)
        {
            return McpJson.Stringify(McpJson.Object(
                "jsonrpc", "2.0",
                "id", id,
                "error", McpJson.Object(
                    "code", code,
                    "message", message)));
        }

        private sealed class HttpRequest
        {
            public readonly string Method;
            public readonly string Path;
            public readonly Dictionary<string, string> Headers;
            public readonly string Body;

            public HttpRequest(string method, string path, Dictionary<string, string> headers, string body)
            {
                Method = method;
                Path = path;
                Headers = headers;
                Body = body;
            }
        }

        private sealed class HttpResponse
        {
            public readonly int StatusCode;
            public readonly string ContentType;
            public readonly string Body;
            public readonly string ExtraHeaders;

            public HttpResponse(int statusCode, string contentType, string body, string extraHeaders = "")
            {
                StatusCode = statusCode;
                ContentType = contentType;
                Body = body;
                ExtraHeaders = extraHeaders ?? string.Empty;
            }
        }
    }
}

