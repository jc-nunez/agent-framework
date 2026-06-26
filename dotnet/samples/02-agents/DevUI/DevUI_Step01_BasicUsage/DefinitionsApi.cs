// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace DevUI_Step01_BasicUsage;

/// <summary>Request body for saving a definition.</summary>
public sealed record SaveDefinitionRequest(string Yaml);

/// <summary>Request body for running a quick test against a definition.</summary>
public sealed record TestDefinitionRequest(string Input);

/// <summary>
/// REST surface for authoring agent + workflow definitions and testing them on the fly.
/// </summary>
/// <remarks>
/// CRUD operations read/write through <see cref="IDefinitionStore"/> (YAML files today). The test
/// endpoints build an agent/workflow from the <em>current</em> stored YAML and run it once, giving
/// instant author→test feedback without a restart. (The DevUI catalog still reflects startup state,
/// since the framework discovers entities from DI at startup.)
/// </remarks>
internal static class DefinitionsApi
{
    public static void MapDefinitionsApi(this WebApplication app)
    {
        // Serve the authoring page from the same host as the DevUI.
        app.MapGet("/authoring", () =>
            Results.Content(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "authoring.html")), "text/html"));

        var agents = app.MapGroup("/api/definitions/agents");
        agents.MapGet("/", (IDefinitionStore store) => Results.Json(store.ListAgents()));
        agents.MapGet("/{name}", (string name, IDefinitionStore store) => GetOne(store.ListAgents(), name));
        agents.MapPut("/{name}", (string name, SaveDefinitionRequest body, IDefinitionStore store) =>
        {
            store.SaveAgent(name, body.Yaml);
            return Results.Ok(new { saved = name });
        });
        agents.MapDelete("/{name}", (string name, IDefinitionStore store) =>
            store.DeleteAgent(name) ? Results.Ok(new { deleted = name }) : Results.NotFound());
        agents.MapPost("/{name}/test", TestAgentAsync);

        var workflows = app.MapGroup("/api/definitions/workflows");
        workflows.MapGet("/", (IDefinitionStore store) => Results.Json(store.ListWorkflows()));
        workflows.MapGet("/{name}", (string name, IDefinitionStore store) => GetOne(store.ListWorkflows(), name));
        workflows.MapPut("/{name}", (string name, SaveDefinitionRequest body, IDefinitionStore store) =>
        {
            store.SaveWorkflow(name, body.Yaml);
            return Results.Ok(new { saved = name });
        });
        workflows.MapDelete("/{name}", (string name, IDefinitionStore store) =>
            store.DeleteWorkflow(name) ? Results.Ok(new { deleted = name }) : Results.NotFound());
        workflows.MapPost("/{name}/test", TestWorkflowAsync);
    }

    private static IResult GetOne(IReadOnlyList<Definition> defs, string name)
    {
        var def = defs.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        return def is null ? Results.NotFound() : Results.Json(def);
    }

    private static async Task<IResult> TestAgentAsync(
        string name,
        TestDefinitionRequest body,
        IDefinitionStore store,
        ChatClientPromptAgentFactory factory)
    {
        if (string.IsNullOrWhiteSpace(body.Input))
        {
            return Results.BadRequest(new { error = "Provide a non-empty 'input' to test." });
        }

        var def = store.ListAgents().FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def is null)
        {
            return Results.NotFound();
        }

        AIAgent agent = await factory.CreateFromYamlAsync(def.Yaml)
            ?? throw new InvalidOperationException($"Agent '{name}' produced no agent.");

        var response = await agent.RunAsync(body.Input);
        return Results.Json(new { output = response.Text });
    }

    private static async Task<IResult> TestWorkflowAsync(
        string name,
        TestDefinitionRequest body,
        IDefinitionStore store,
        ChatClientPromptAgentFactory factory,
        IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(body.Input))
        {
            return Results.BadRequest(new { error = "Provide a non-empty 'input' to test." });
        }

        var def = store.ListWorkflows().FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        if (def is null)
        {
            return Results.NotFound();
        }

        // Rebuild the agent registry from the CURRENT stored definitions so the workflow test
        // reflects any just-saved agent edits (reuse seam: workflows resolve agents by name).
        var registry = new Dictionary<string, AIAgent>(StringComparer.OrdinalIgnoreCase);
        foreach (var agentDef in store.ListAgents())
        {
            AIAgent agent = await factory.CreateFromYamlAsync(agentDef.Yaml)
                ?? throw new InvalidOperationException($"Agent '{agentDef.Name}' produced no agent.");
            registry[agent.Name ?? agentDef.Name] = agent;
        }

        var options = new DeclarativeWorkflowOptions(new LocalResponseAgentProvider(registry))
        {
            Configuration = configuration,
        };

        Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(new StringReader(def.Yaml), options);
        AIAgent workflowAgent = workflow.AsAIAgent(name: name, includeWorkflowOutputsInResponse: true);

        var response = await workflowAgent.RunAsync(body.Input);
        return Results.Json(new { output = response.Text });
    }
}
