using System;
using System.Collections.Generic;

namespace StrangeApe.OpenUnityMcp
{
    internal static class McpProtocol
    {
        private const string LatestProtocolVersion = "2025-11-25";

        private static readonly HashSet<string> SupportedProtocolVersions = new HashSet<string>(StringComparer.Ordinal)
        {
            "2025-11-25",
            "2025-06-18",
            "2024-11-05"
        };

        public static McpProtocolResponse Handle(string body)
        {
            object id = null;

            try
            {
                var parsed = McpJson.Parse(body);
                var request = parsed as Dictionary<string, object>;
                if (request == null)
                {
                    return JsonError(null, -32600, "JSON-RPC body must be an object.", 400);
                }

                request.TryGetValue("id", out id);
                var method = McpJson.AsString(request, "method");
                if (string.IsNullOrEmpty(method))
                {
                    return new McpProtocolResponse(202, string.Empty, true);
                }

                var isNotification = !request.ContainsKey("id");
                if (isNotification)
                {
                    return new McpProtocolResponse(202, string.Empty, true);
                }

                var parameters = McpJson.AsObject(request.TryGetValue("params", out var paramsValue) ? paramsValue : null);

                switch (method)
                {
                    case "initialize":
                        return JsonResult(id, Initialize(parameters));
                    case "ping":
                        return JsonResult(id, McpJson.Object());
                    case "prompts/list":
                        return JsonResult(id, McpPromptRegistry.ListPrompts());
                    case "prompts/get":
                        return HandlePromptGet(id, parameters);
                    case "resources/list":
                        return JsonResult(id, McpResourceRegistry.ListResources());
                    case "resources/read":
                        return HandleResourceRead(id, parameters);
                    case "tools/list":
                        return JsonResult(id, McpToolRegistry.ListTools());
                    case "tools/call":
                        return HandleToolCall(id, parameters);
                    default:
                        return JsonError(id, -32601, "Method not found: " + method, 200);
                }
            }
            catch (FormatException ex)
            {
                return JsonError(id, -32700, ex.Message, 400);
            }
            catch (McpResourceNotFoundException ex)
            {
                return JsonError(id, -32002, ex.Message, 200);
            }
            catch (ArgumentException ex)
            {
                return JsonError(id, -32602, ex.Message, 200);
            }
            catch (Exception ex)
            {
                return JsonError(id, -32603, ex.Message, 200);
            }
        }

        private static Dictionary<string, object> Initialize(Dictionary<string, object> parameters)
        {
            var requestedVersion = McpJson.AsString(parameters, "protocolVersion", LatestProtocolVersion);
            var negotiatedVersion = SupportedProtocolVersions.Contains(requestedVersion) ? requestedVersion : LatestProtocolVersion;

            return McpJson.Object(
                "protocolVersion", negotiatedVersion,
                "capabilities", McpJson.Object(
                    "prompts", McpJson.Object(
                        "listChanged", false),
                    "resources", McpJson.Object(
                        "subscribe", false,
                        "listChanged", false),
                    "tools", McpJson.Object(
                        "listChanged", false)),
                "serverInfo", McpJson.Object(
                    "name", "open-unity-mcp",
                    "title", "Open Unity MCP",
                    "version", "0.7.0",
                    "description", "A small in-editor MCP server for Unity."),
                "instructions", "Use Unity tools for project inspection and editor automation. Most file writes are limited to Assets and Packages. The unity.execute_csharp fallback runs unrestricted editor C# and should be approval-gated.");
        }

        private static McpProtocolResponse HandlePromptGet(object id, Dictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                return JsonError(id, -32602, "Missing prompts/get params.", 200);
            }

            var name = McpJson.AsString(parameters, "name");
            if (string.IsNullOrEmpty(name))
            {
                return JsonError(id, -32602, "Missing prompt name.", 200);
            }

            var arguments = McpJson.AsObject(parameters.TryGetValue("arguments", out var argsValue) ? argsValue : null) ??
                            new Dictionary<string, object>(StringComparer.Ordinal);
            return JsonResult(id, McpPromptRegistry.GetPrompt(name, arguments));
        }

        private static McpProtocolResponse HandleResourceRead(object id, Dictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                return JsonError(id, -32602, "Missing resources/read params.", 200);
            }

            var uri = McpJson.AsString(parameters, "uri");
            if (string.IsNullOrEmpty(uri))
            {
                return JsonError(id, -32602, "Missing resource uri.", 200);
            }

            return JsonResult(id, McpResourceRegistry.ReadResource(uri));
        }

        private static McpProtocolResponse HandleToolCall(object id, Dictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                return JsonError(id, -32602, "Missing tools/call params.", 200);
            }

            var name = McpJson.AsString(parameters, "name");
            if (string.IsNullOrEmpty(name))
            {
                return JsonError(id, -32602, "Missing tool name.", 200);
            }

            var arguments = McpJson.AsObject(parameters.TryGetValue("arguments", out var argsValue) ? argsValue : null) ??
                            new Dictionary<string, object>(StringComparer.Ordinal);
            var result = McpToolRegistry.Call(name, arguments);
            return JsonResult(id, result);
        }

        private static McpProtocolResponse JsonResult(object id, object result)
        {
            return new McpProtocolResponse(200, McpJson.Stringify(McpJson.Object(
                "jsonrpc", "2.0",
                "id", id,
                "result", result)), false);
        }

        private static McpProtocolResponse JsonError(object id, int code, string message, int httpStatus)
        {
            return new McpProtocolResponse(httpStatus, McpJson.Stringify(McpJson.Object(
                "jsonrpc", "2.0",
                "id", id,
                "error", McpJson.Object(
                    "code", code,
                    "message", message))), false);
        }
    }

    internal sealed class McpProtocolResponse
    {
        public readonly int HttpStatus;
        public readonly string Body;
        public readonly bool AcceptedNotification;

        public McpProtocolResponse(int httpStatus, string body, bool acceptedNotification)
        {
            HttpStatus = httpStatus;
            Body = body;
            AcceptedNotification = acceptedNotification;
        }
    }
}
