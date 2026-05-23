using System;
using System.Collections.Generic;
using UnityEngine;

namespace StrangeApe.OpenUnityMcp
{
    internal sealed class McpPrompt
    {
        public readonly string Name;
        public readonly string Title;
        public readonly string Description;
        public readonly List<object> Arguments;
        public readonly Func<Dictionary<string, object>, Dictionary<string, object>> Get;

        public McpPrompt(string name, string title, string description, List<object> arguments, Func<Dictionary<string, object>, Dictionary<string, object>> get)
        {
            Name = name;
            Title = title;
            Description = description;
            Arguments = arguments;
            Get = get;
        }
    }

    internal static class McpPromptRegistry
    {
        private static readonly McpPrompt[] Prompts =
        {
            new McpPrompt(
                "unity_editor_task",
                "Unity Editor Task",
                "Plan and execute a Unity editor task using Open Unity MCP tools.",
                McpJson.Array(
                    McpJson.Object("name", "task", "description", "The Unity task to perform.", "required", true),
                    McpJson.Object("name", "constraints", "description", "Optional constraints such as no play mode or no writes.", "required", false)),
                GetUnityEditorTaskPrompt),
            new McpPrompt(
                "unity_code_review",
                "Unity Code Review",
                "Review Unity scripts or package changes with MCP-aware checks.",
                McpJson.Array(
                    McpJson.Object("name", "focus", "description", "Optional review focus, such as performance, editor safety, or package API.", "required", false)),
                GetUnityCodeReviewPrompt)
        };

        static McpPromptRegistry()
        {
            Array.Sort(Prompts, (left, right) => string.CompareOrdinal(left.Name, right.Name));
        }

        public static Dictionary<string, object> ListPrompts()
        {
            var prompts = new List<object>();
            foreach (var prompt in Prompts)
            {
                prompts.Add(McpJson.Object(
                    "name", prompt.Name,
                    "title", prompt.Title,
                    "description", prompt.Description,
                    "arguments", prompt.Arguments));
            }

            return McpJson.Object("prompts", prompts);
        }

        public static Dictionary<string, object> GetPrompt(string name, Dictionary<string, object> arguments)
        {
            foreach (var prompt in Prompts)
            {
                if (string.Equals(prompt.Name, name, StringComparison.Ordinal))
                {
                    return prompt.Get(arguments ?? new Dictionary<string, object>(StringComparer.Ordinal));
                }
            }

            throw new ArgumentException("Unknown prompt: " + name);
        }

        private static Dictionary<string, object> GetUnityEditorTaskPrompt(Dictionary<string, object> arguments)
        {
            var task = McpJson.AsString(arguments, "task");
            if (string.IsNullOrEmpty(task))
            {
                throw new ArgumentException("Missing required prompt argument: task");
            }

            var constraints = McpJson.AsString(arguments, "constraints", "Ask before writes, play mode changes, prefab changes, or build actions.");
            var text =
                "You are working inside a Unity Editor project through Open Unity MCP.\n\n" +
                "Task:\n" + task + "\n\n" +
                "Constraints:\n" + constraints + "\n\n" +
                "Use read-only resources first: unity://project/info, unity://scene/open-scenes, and unity://editor/selection. " +
                "Prefer targeted tools over broad edits. Before mutating assets, scenes, prefabs, components, play mode, or menu items, explain the intended action and use the narrowest tool available. " +
                "Keep generated files under Assets or Packages and preserve Unity .meta files.";

            return PromptResult("Unity editor task prompt", text);
        }

        private static Dictionary<string, object> GetUnityCodeReviewPrompt(Dictionary<string, object> arguments)
        {
            var focus = McpJson.AsString(arguments, "focus", "correctness, editor safety, package installability, performance, and test coverage");
            var text =
                "Review the Unity package changes with focus on " + focus + ".\n\n" +
                "Check for: Unity API calls from background threads, unsafe filesystem paths, missing .meta files, unbounded result sizes, domain reload cleanup, " +
                "MCP schema compatibility, package.json correctness, Git-installable layout, and edit-mode test coverage. " +
                "Prioritize concrete bugs and public-release blockers over style.";

            return PromptResult("Unity code review prompt", text);
        }

        private static Dictionary<string, object> PromptResult(string description, string text)
        {
            return McpJson.Object(
                "description", description,
                "messages", McpJson.Array(McpJson.Object(
                    "role", "user",
                    "content", McpJson.Object(
                        "type", "text",
                        "text", text))));
        }
    }
}

