using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace StrangeApe.OpenUnityMcp.Tests
{
    // Exercises the opt-in access-token gate on POST /mcp. Follows McpServerHttpTests'
    // park/restore pattern (the server is a process-wide singleton) and ADDITIONALLY
    // snapshots and restores the RequireAccessToken/AccessToken EditorPrefs so the live
    // session — which connects tokenless — is never left with enforcement turned on.
    public sealed class McpServerAccessTokenTests
    {
        private const string RequireAccessTokenKey = "StrangeApe.OpenUnityMcp.RequireAccessToken";
        private const string AccessTokenKey = "StrangeApe.OpenUnityMcp.AccessToken";

        private bool _serverWasRunning;
        private int _previousPort;
        private bool _hadRequirePref;
        private bool _previousRequire;
        private bool _hadTokenPref;
        private string _previousToken;

        [SetUp]
        public void SetUp()
        {
            _serverWasRunning = OpenUnityMcpServer.IsRunning;
            _previousPort = OpenUnityMcpServer.Port;

            _hadRequirePref = EditorPrefs.HasKey(RequireAccessTokenKey);
            _previousRequire = EditorPrefs.GetBool(RequireAccessTokenKey, false);
            _hadTokenPref = EditorPrefs.HasKey(AccessTokenKey);
            _previousToken = EditorPrefs.GetString(AccessTokenKey, string.Empty);

            OpenUnityMcpServer.Stop();
        }

        [TearDown]
        public void TearDown()
        {
            OpenUnityMcpServer.Stop();

            // Restore the auth prefs exactly as they were so the live tokenless session
            // is unaffected.
            RestoreBoolPref(RequireAccessTokenKey, _hadRequirePref, _previousRequire);
            RestoreStringPref(AccessTokenKey, _hadTokenPref, _previousToken);

            if (_serverWasRunning && _previousPort > 0)
            {
                OpenUnityMcpServer.Start(_previousPort);
            }
        }

        [UnityTest]
        public IEnumerator EnforcementOff_TokenlessPostSucceeds()
        {
            OpenUnityMcpSettings.RequireAccessToken = false;
            var port = FindFreePort();
            OpenUnityMcpServer.Start(port);

            var task = PostAsync(port, InitializeBody, null, null);
            yield return WaitForTask(task);

            Assert.AreEqual(200, task.Result.Status);
            StringAssert.Contains("\"open-unity-mcp\"", task.Result.Body);
        }

        [UnityTest]
        public IEnumerator EnforcementOn_TokenlessPostIsRejectedWith401()
        {
            OpenUnityMcpSettings.RequireAccessToken = true;
            _ = OpenUnityMcpSettings.AccessToken; // ensure a token exists
            var port = FindFreePort();
            OpenUnityMcpServer.Start(port);

            var task = PostAsync(port, InitializeBody, null, null);
            yield return WaitForTask(task);

            Assert.AreEqual(401, task.Result.Status);
            StringAssert.Contains("access token", task.Result.Body);
        }

        [UnityTest]
        public IEnumerator EnforcementOn_AuthorizationBearerHeaderAccepted()
        {
            OpenUnityMcpSettings.RequireAccessToken = true;
            var token = OpenUnityMcpSettings.AccessToken;
            var port = FindFreePort();
            OpenUnityMcpServer.Start(port);

            var task = PostAsync(port, InitializeBody, "Bearer " + token, null);
            yield return WaitForTask(task);

            Assert.AreEqual(200, task.Result.Status);
            StringAssert.Contains("\"open-unity-mcp\"", task.Result.Body);
        }

        [UnityTest]
        public IEnumerator EnforcementOn_XHeaderAccepted()
        {
            OpenUnityMcpSettings.RequireAccessToken = true;
            var token = OpenUnityMcpSettings.AccessToken;
            var port = FindFreePort();
            OpenUnityMcpServer.Start(port);

            var task = PostAsync(port, InitializeBody, null, token);
            yield return WaitForTask(task);

            Assert.AreEqual(200, task.Result.Status);
            StringAssert.Contains("\"open-unity-mcp\"", task.Result.Body);
        }

        [UnityTest]
        public IEnumerator EnforcementOn_WrongTokenRejectedWith401()
        {
            OpenUnityMcpSettings.RequireAccessToken = true;
            _ = OpenUnityMcpSettings.AccessToken;
            var port = FindFreePort();
            OpenUnityMcpServer.Start(port);

            var task = PostAsync(port, InitializeBody, "Bearer deadbeefdeadbeef", null);
            yield return WaitForTask(task);

            Assert.AreEqual(401, task.Result.Status);
        }

        [UnityTest]
        public IEnumerator EnforcementOn_HealthStaysOpen()
        {
            OpenUnityMcpSettings.RequireAccessToken = true;
            _ = OpenUnityMcpSettings.AccessToken;
            var port = FindFreePort();
            OpenUnityMcpServer.Start(port);

            var task = GetAsync(port, "/health");
            yield return WaitForTask(task);

            Assert.AreEqual(200, task.Result.Status);
            StringAssert.Contains("\"ok\":true", task.Result.Body);
        }

        [UnityTest]
        public IEnumerator EnforcementSnapshotIsCapturedAtStart_NotPerRequest()
        {
            // A settings change must take effect only on the next server start: the
            // server snapshots the flag/token on Start(). Start with enforcement off,
            // flip the pref on while running, and confirm the running server still
            // accepts a tokenless request (the change is deferred to restart).
            OpenUnityMcpSettings.RequireAccessToken = false;
            var port = FindFreePort();
            OpenUnityMcpServer.Start(port);

            OpenUnityMcpSettings.RequireAccessToken = true;

            var task = PostAsync(port, InitializeBody, null, null);
            yield return WaitForTask(task);

            Assert.AreEqual(200, task.Result.Status,
                "Flipping the pref on a running server must not retroactively enforce; it applies on next start.");
        }

        private const string InitializeBody =
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}";

        private static void RestoreBoolPref(string key, bool had, bool value)
        {
            if (had)
            {
                EditorPrefs.SetBool(key, value);
            }
            else
            {
                EditorPrefs.DeleteKey(key);
            }
        }

        private static void RestoreStringPref(string key, bool had, string value)
        {
            if (had)
            {
                EditorPrefs.SetString(key, value);
            }
            else
            {
                EditorPrefs.DeleteKey(key);
            }
        }

        private struct HttpResult
        {
            public int Status;
            public string Body;
        }

        private static Task<HttpResult> PostAsync(int port, string body, string authorization, string xToken)
        {
            return Task.Run(() =>
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/mcp");
                request.Method = "POST";
                request.ContentType = "application/json";
                if (!string.IsNullOrEmpty(authorization))
                {
                    request.Headers["Authorization"] = authorization;
                }

                if (!string.IsNullOrEmpty(xToken))
                {
                    request.Headers["X-Open-Unity-Mcp-Token"] = xToken;
                }

                var bytes = Encoding.UTF8.GetBytes(body);
                request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                return Send(request);
            });
        }

        private static Task<HttpResult> GetAsync(int port, string path)
        {
            return Task.Run(() =>
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + path);
                request.Method = "GET";
                return Send(request);
            });
        }

        private static HttpResult Send(HttpWebRequest request)
        {
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    return new HttpResult { Status = (int)response.StatusCode, Body = reader.ReadToEnd() };
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse httpResponse)
            {
                // 401 / other error statuses surface as WebException; read the body so
                // the test can assert on both status and payload.
                using (httpResponse)
                using (var stream = httpResponse.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    return new HttpResult { Status = (int)httpResponse.StatusCode, Body = reader.ReadToEnd() };
                }
            }
        }

        private static IEnumerator WaitForTask(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                yield return null;
            }

            if (!task.IsCompleted)
            {
                Assert.Fail("Timed out waiting for HTTP request task.");
            }

            if (task.IsFaulted)
            {
                throw task.Exception.GetBaseException();
            }
        }

        private static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
