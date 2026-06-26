// Copyright (c) Microsoft. All rights reserved.

namespace DevUI_Step01_BasicUsage;

/// <summary>A stored definition: its logical name and raw YAML body.</summary>
internal sealed record Definition(string Name, string Yaml);

/// <summary>
/// Persistence seam for agent and workflow definitions.
/// </summary>
/// <remarks>
/// Today this is backed by YAML files on disk (<see cref="FileDefinitionStore"/>). Because the
/// rest of the app depends only on this interface, swapping in a database-backed store later is a
/// one-class change. The Save* methods exist for the UI editing slice (forms/editor → store).
/// </remarks>
internal interface IDefinitionStore
{
    IReadOnlyList<Definition> ListAgents();

    IReadOnlyList<Definition> ListWorkflows();

    void SaveAgent(string name, string yaml);

    void SaveWorkflow(string name, string yaml);

    bool DeleteAgent(string name);

    bool DeleteWorkflow(string name);
}

/// <summary>
/// File-backed <see cref="IDefinitionStore"/>: agents live in <c>{root}/agents/*.yaml</c> and
/// workflows in <c>{root}/workflows/*.yaml</c>. The definition name is the file name (no extension).
/// </summary>
internal sealed class FileDefinitionStore(string rootDirectory) : IDefinitionStore
{
    private string AgentsDir => Path.Combine(rootDirectory, "agents");
    private string WorkflowsDir => Path.Combine(rootDirectory, "workflows");

    public IReadOnlyList<Definition> ListAgents() => Read(this.AgentsDir);

    public IReadOnlyList<Definition> ListWorkflows() => Read(this.WorkflowsDir);

    public void SaveAgent(string name, string yaml) => Write(this.AgentsDir, name, yaml);

    public void SaveWorkflow(string name, string yaml) => Write(this.WorkflowsDir, name, yaml);

    public bool DeleteAgent(string name) => Delete(this.AgentsDir, name);

    public bool DeleteWorkflow(string name) => Delete(this.WorkflowsDir, name);

    private static IReadOnlyList<Definition> Read(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory
            .EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new Definition(Path.GetFileNameWithoutExtension(path), File.ReadAllText(path)))];
    }

    private static void Write(string directory, string name, string yaml)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{SafeName(name)}.yaml"), yaml);
    }

    private static bool Delete(string directory, string name)
    {
        var path = Path.Combine(directory, $"{SafeName(name)}.yaml");
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    // Guard against path traversal: a definition name must be a bare file name.
    private static string SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"Invalid definition name: '{name}'.", nameof(name));
        }

        return name;
    }
}
