// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevUI_Step01_BasicUsage;

/// <summary>
/// A live, in-memory catalog of definition-driven agents and workflows, rebuilt from the
/// <see cref="IDefinitionStore"/> on demand. Holds an immutable snapshot that is swapped atomically,
/// so readers (DevUI discovery + the Responses run-router, via the Func seams) never see a partial state.
/// </summary>
/// <remarks>
/// This is what makes file hot-reload possible: when definition files change, <see cref="ReloadAsync"/>
/// rebuilds the snapshot and the framework — which reads agents/workflows through dynamic Func sources —
/// reflects the change on the next request, with no process restart.
/// </remarks>
internal sealed class DefinitionCatalog(
    IDefinitionStore store,
    ChatClientPromptAgentFactory agentFactory,
    IConfiguration configuration,
    ILogger<DefinitionCatalog> logger)
{
    private volatile Snapshot _snapshot = Snapshot.Empty;

    /// <summary>Agents discoverable in the DevUI: agent definitions plus workflows-as-agents (runnable).</summary>
    public IEnumerable<AIAgent> Agents => this._snapshot.AllAgents;

    /// <summary>Workflows discoverable in the DevUI (for the graph view).</summary>
    public IEnumerable<Workflow> Workflows => this._snapshot.Workflows;

    /// <summary>Resolves a runnable agent (agent definition or workflow) by name, or null.</summary>
    public AIAgent? Resolve(string name) => this._snapshot.ByName.GetValueOrDefault(name);

    /// <summary>Rebuilds the catalog from the current stored definitions.</summary>
    public async Task ReloadAsync()
    {
        // 1) Build agent definitions into a shared name -> agent registry.
        var agents = new Dictionary<string, AIAgent>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in store.ListAgents())
        {
            try
            {
                AIAgent agent = await agentFactory.CreateFromYamlAsync(def.Yaml).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("factory returned null");
                agents[agent.Name ?? def.Name] = agent;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping invalid agent definition '{Name}'", def.Name);
            }
        }

        // 2) One provider over the registry resolves agents (by name) for every workflow.
        var options = new DeclarativeWorkflowOptions(new LocalResponseAgentProvider(agents))
        {
            Configuration = configuration,
        };

        // 3) Build workflows; expose each both as a Workflow (graph) and an AIAgent (runnable).
        var workflows = new List<Workflow>();
        var workflowAgents = new Dictionary<string, AIAgent>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in store.ListWorkflows())
        {
            try
            {
                Workflow workflow = SetWorkflowName(
                    DeclarativeWorkflowBuilder.Build<string>(new StringReader(def.Yaml), options),
                    def.Name);
                workflows.Add(workflow);
                workflowAgents[def.Name] = workflow.AsAIAgent(name: def.Name, includeWorkflowOutputsInResponse: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping invalid workflow definition '{Name}'", def.Name);
            }
        }

        // 4) Index everything runnable by name (agents + workflows), then swap the snapshot atomically.
        var byName = new Dictionary<string, AIAgent>(agents, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, agent) in workflowAgents)
        {
            byName[name] = agent;
        }

        this._snapshot = new Snapshot(
            [.. agents.Values, .. workflowAgents.Values],
            workflows,
            byName);

        logger.LogInformation("Definition catalog reloaded: {Agents} agents, {Workflows} workflows.", agents.Count, workflows.Count);
    }

    // Workflow.Name has an internal init-only setter and DeclarativeWorkflowBuilder exposes no naming
    // hook, so stamp it reflectively to keep the graph entity id and the runnable agent name aligned.
    private static Workflow SetWorkflowName(Workflow workflow, string name)
    {
        typeof(Workflow).GetProperty(nameof(Workflow.Name))!.SetValue(workflow, name);
        return workflow;
    }

    private sealed record Snapshot(
        IReadOnlyList<AIAgent> AllAgents,
        IReadOnlyList<Workflow> Workflows,
        IReadOnlyDictionary<string, AIAgent> ByName)
    {
        public static readonly Snapshot Empty = new([], [], new Dictionary<string, AIAgent>());
    }
}
