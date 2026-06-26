// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates basic usage of the DevUI in an ASP.NET Core application with AI agents.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace DevUI_Step01_BasicUsage;

/// <summary>
/// Sample demonstrating basic usage of the DevUI in an ASP.NET Core application.
/// </summary>
/// <remarks>
/// This sample shows how to:
/// 1. Set up Azure OpenAI as the chat client
/// 2. Create function tools for agents to use
/// 3. Register agents and workflows using the hosting packages with tools
/// 4. Map the DevUI endpoint which automatically configures the middleware
/// 5. Map the dynamic OpenAI Responses API for Python DevUI compatibility
/// 6. Access the DevUI in a web browser
///
/// The DevUI provides an interactive web interface for testing and debugging AI agents.
/// DevUI assets are served from embedded resources within the assembly.
/// Simply call MapDevUI() to set up everything needed.
///
/// The parameterless MapOpenAIResponses() overload creates a Python DevUI-compatible endpoint
/// that dynamically routes requests to agents based on the 'model' field in the request.
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Entry point that starts an ASP.NET Core web server with the DevUI.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Set up the Ollama chat client for local models.
        // Defaults target a local Ollama instance running qwen3:8b; override via OLLAMA_ENDPOINT / OLLAMA_MODEL_NAME.
        var endpoint = builder.Configuration["OLLAMA_ENDPOINT"] ?? "http://localhost:11434";
        var modelName = builder.Configuration["OLLAMA_MODEL_NAME"] ?? "qwen3:8b";

        // OllamaApiClient implements IChatClient, so it plugs directly into the agent hosting stack.
        IChatClient chatClient = new OllamaApiClient(new Uri(endpoint), modelName);

        builder.Services.AddChatClient(chatClient);

        // Define some example tools
        [Description("Get the weather for a given location.")]
        static string GetWeather([Description("The location to get the weather for.")] string location)
            => $"The weather in {location} is cloudy with a high of 15°C.";

        [Description("Calculate the sum of two numbers.")]
        static double Add([Description("The first number.")] double a, [Description("The second number.")] double b)
            => a + b;

        [Description("Get the current time.")]
        static string GetCurrentTime()
            => DateTime.Now.ToString("HH:mm:ss");

        // Register sample agents with tools
        builder.AddAIAgent("assistant", "You are a helpful assistant. Answer questions concisely and accurately.")
            .WithAITools(
                AIFunctionFactory.Create(GetWeather, name: "get_weather"),
                AIFunctionFactory.Create(GetCurrentTime, name: "get_current_time")
            );

        builder.AddAIAgent("poet", "You are a creative poet. Respond to all requests with beautiful poetry.");

        builder.AddAIAgent("coder", "You are an expert programmer. Help users with coding questions and provide code examples.")
            .WithAITool(AIFunctionFactory.Create(Add, name: "add"));

        // Register sample workflows
        var assistantBuilder = builder.AddAIAgent("workflow-assistant", "You are a helpful assistant in a workflow.");
        var reviewerBuilder = builder.AddAIAgent("workflow-reviewer", "You are a reviewer. Review and critique the previous response.");
        builder.AddWorkflow("review-workflow", (sp, key) =>
        {
            var agents = new List<IHostedAgentBuilder>() { assistantBuilder, reviewerBuilder }.Select(ab => sp.GetRequiredKeyedService<AIAgent>(ab.Name));
            return AgentWorkflowBuilder.BuildSequential(workflowName: key, agents: agents);
        }).AddAsAIAgent();

        // ----------------------------------------------------------------------------------
        // DEFINITION-DRIVEN agents + workflows (the product model), with FILE HOT-RELOAD.
        //
        // Definitions are authored as data (YAML) and persisted via IDefinitionStore (file-backed
        // today, DB-swappable later). A DefinitionCatalog rebuilds agents + workflows from the store
        // into an atomic snapshot, and a file watcher reloads it on change. The framework discovers
        // and runs them through dynamic Func seams (registered below), so authoring — via the form
        // or by editing YAML — shows up live in the DevUI with NO restart.
        // ----------------------------------------------------------------------------------
        var definitionsDir = Path.Combine(builder.Environment.ContentRootPath, "definitions");
#pragma warning disable CA1859 // Intentionally typed as the abstraction: the store is swappable (file -> DB) later.
        IDefinitionStore definitionStore = new FileDefinitionStore(definitionsDir);
#pragma warning restore CA1859

        // Tool catalog available to declarative agents (agent YAML opts in via its `tools:` list).
        var agentFactory = new ChatClientPromptAgentFactory(chatClient);

        builder.Services.AddSingleton(definitionStore);
        builder.Services.AddSingleton(agentFactory);
        builder.Services.AddSingleton(sp => new DefinitionCatalog(
            sp.GetRequiredService<IDefinitionStore>(),
            sp.GetRequiredService<ChatClientPromptAgentFactory>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILogger<DefinitionCatalog>>()));

        // Dynamic discovery + run-routing seams: the framework consults these per request, so the
        // catalog's current contents (not a startup snapshot) drive the DevUI list and run resolution.
        builder.Services.AddSingleton<Func<IEnumerable<AIAgent>>>(sp => () => sp.GetRequiredService<DefinitionCatalog>().Agents);
        builder.Services.AddSingleton<Func<IEnumerable<Workflow>>>(sp => () => sp.GetRequiredService<DefinitionCatalog>().Workflows);
        builder.Services.AddSingleton<Func<string, AIAgent?>>(sp => name => sp.GetRequiredService<DefinitionCatalog>().Resolve(name));

        // Watches the definitions directory and reloads the catalog on change (debounced).
        builder.Services.AddSingleton(sp => new DefinitionWatcher(
            definitionsDir,
            sp.GetRequiredService<DefinitionCatalog>(),
            sp.GetRequiredService<ILogger<DefinitionWatcher>>()));

        // Register DevUI services (auth filter, options, etc.) — required before MapDevUI().
        if (builder.Environment.IsDevelopment())
        {
            builder.AddDevUI();
        }

        builder.Services.AddOpenAIResponses();
        builder.Services.AddOpenAIConversations();

        var app = builder.Build();

        app.MapOpenAIResponses();
        app.MapOpenAIConversations();

        if (builder.Environment.IsDevelopment())
        {
            app.MapDevUI();
        }

        // Authoring surface: CRUD + on-the-fly test for agent/workflow definitions, plus /authoring page.
        app.MapDefinitionsApi();

        // Initial load of definitions, then start watching for changes (file hot-reload).
        await app.Services.GetRequiredService<DefinitionCatalog>().ReloadAsync();
        _ = app.Services.GetRequiredService<DefinitionWatcher>();

        Console.WriteLine("DevUI is available at: https://localhost:50516/devui");
        Console.WriteLine("Authoring UI is available at: http://localhost:50518/authoring");
        Console.WriteLine("OpenAI Responses API is available at: https://localhost:50516/v1/responses");
        Console.WriteLine("Definitions hot-reload from: " + definitionsDir);
        Console.WriteLine("Press Ctrl+C to stop the server.");

        app.Run();
    }
}
