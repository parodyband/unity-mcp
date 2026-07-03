using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace StrangeApe.OpenUnityMcp.Tests
{
    public sealed class McpServerHttpTests
    {
        private bool _serverWasRunning;
        private int _previousPort;

        [SetUp]
        public void SetUp()
        {
            // The server is a process-wide singleton. If a live session already has it running,
            // Start(testPort) would silently no-op (test connects to an unbound port) and
            // TearDown's Stop() would kill the live server. Park it and restore it afterwards.
            _serverWasRunning = OpenUnityMcpServer.IsRunning;
            _previousPort = OpenUnityMcpServer.Port;
            OpenUnityMcpServer.Stop();
        }

        [TearDown]
        public void TearDown()
        {
            OpenUnityMcpServer.Stop();
            if (_serverWasRunning && _previousPort > 0)
            {
                OpenUnityMcpServer.Start(_previousPort);
            }
        }

        [UnityTest]
        public IEnumerator HttpEndpointHandlesInitializeAndToolsList()
        {
            var port = FindFreePort();
            OpenUnityMcpServer.Start(port);

            var initializeTask = PostAsync(port, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");
            yield return WaitForTask(initializeTask);

            var initialize = initializeTask.Result;
            StringAssert.Contains("\"open-unity-mcp\"", initialize);
            StringAssert.Contains("\"protocolVersion\":\"2025-06-18\"", initialize);

            var toolsTask = PostAsync(port, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
            yield return WaitForTask(toolsTask);

            var tools = toolsTask.Result;
            StringAssert.Contains("\"unity.get_project_info\"", tools);
            StringAssert.Contains("\"unity.write_asset_text\"", tools);
        }

        private static Task<string> PostAsync(int port, string body)
        {
            return Task.Run(() => Post(port, body));
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

            if (task.IsCanceled)
            {
                Assert.Fail("HTTP request task was canceled.");
            }

            if (task.IsFaulted)
            {
                throw task.Exception.GetBaseException();
            }
        }

        private static string Post(int port, string body)
        {
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/mcp");
            request.Method = "POST";
            request.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = bytes.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                return reader.ReadToEnd();
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
