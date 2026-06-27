// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace CodingAgent.Web;

/// <summary>
/// File tools confined to the agent's sandbox workspace. Every path is resolved and validated to
/// stay inside the workspace root, so the agent can never read or write outside its sandbox.
/// </summary>
internal static class FileTools
{
    public static IReadOnlyList<AIFunction> Create(string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);

        string Resolve(string path)
        {
            var full = Path.GetFullPath(Path.Combine(root, path));
            if (!full.Equals(root, StringComparison.Ordinal) &&
                !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Path '{path}' escapes the workspace sandbox.");
            }

            return full;
        }

        [Description("List files and directories under a workspace-relative path (default: the workspace root).")]
        string ListDir([Description("Workspace-relative directory path.")] string path = ".")
        {
            var dir = Resolve(path);
            if (!Directory.Exists(dir))
            {
                return $"Directory not found: {path}";
            }

            var entries = Directory.EnumerateFileSystemEntries(dir)
                .Select(p => (Directory.Exists(p) ? "dir  " : "file ") + Path.GetRelativePath(root, p))
                .OrderBy(s => s, StringComparer.Ordinal);
            return string.Join("\n", entries);
        }

        [Description("Read a text file from the workspace.")]
        string ReadFile([Description("Workspace-relative file path.")] string path)
        {
            var file = Resolve(path);
            return File.Exists(file) ? File.ReadAllText(file) : $"File not found: {path}";
        }

        [Description("Create or overwrite a text file in the workspace.")]
        string WriteFile(
            [Description("Workspace-relative file path.")] string path,
            [Description("Full file contents to write.")] string content)
        {
            var file = Resolve(path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
            return $"Wrote {content.Length} chars to {path}";
        }

        return
        [
            AIFunctionFactory.Create(ListDir, "list_dir"),
            AIFunctionFactory.Create(ReadFile, "read_file"),
            AIFunctionFactory.Create(WriteFile, "write_file"),
        ];
    }
}
