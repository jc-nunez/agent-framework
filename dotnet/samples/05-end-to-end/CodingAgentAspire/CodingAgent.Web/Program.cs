// Copyright (c) Microsoft. All rights reserved.

// A production-shaped coding agent, testable in the DevUI and (next increment) orchestrated by Aspire.
//
// This increment wires the runnable core:
//   - a coding agent (ChatClientAgent + function invocation) on a local Ollama model,
//   - a DOCKER-SANDBOXED shell tool (isolated container, workspace mounted read/write, approval-gated),
//   - workspace-confined file tools (read/list/write),
//   - a tool-audit HOOK around every tool call.
// Next increments add: sub-agents-as-tools, skills (AgentSkillsProvider), an ask_user tool, and the
// Aspire AppHost.

using System.ClientModel;
using Anthropic;
using CodingAgent.Web;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using OpenTelemetry;
using OpenTelemetry.Trace;

// --- Telemetry: enable OpenTelemetry tracing with `--instrumentation` (or INSTRUMENTATION=true) ---
// Spans for the agent run, each tool call, and the model request stream to the console (and to an
// OTLP collector / the Aspire dashboard if OTEL_EXPORTER_OTLP_ENDPOINT is set).
const string TelemetrySource = "CodingAgent";
bool instrumentation =
    args.Contains("--instrumentation") ||
    string.Equals(System.Environment.GetEnvironmentVariable("INSTRUMENTATION"), "true", StringComparison.OrdinalIgnoreCase);

// Strip our custom flag so the host's command-line config parser ignores the valueless switch.
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--instrumentation").ToArray());

// Aspire service defaults: OpenTelemetry (registers the "CodingAgent" source and exports to the
// Aspire dashboard via OTLP when run under the AppHost), health checks, service discovery, resilience.
builder.AddServiceDefaults();

// For standalone runs, `--instrumentation` also streams the same spans to the console.
if (instrumentation)
{
    builder.Services.AddOpenTelemetry().WithTracing(t => t.AddConsoleExporter());
}

// --- Model provider: Kimi Code (a real coding model) when a key is configured, else local Ollama ---
// Set the key via user-secrets/env (KIMI_API_KEY) — never hardcode it. KIMI_TRANSPORT selects the API:
//   "anthropic" (default) -> Kimi's Anthropic-compatible endpoint (unlocks K2.7 + thinking; best for coding)
//   "openai"              -> Kimi's OpenAI-compatible endpoint (portable; routes to K2.6)
IChatClient chatClient;
string activeModel;
string activeProvider;
var kimiKey = builder.Configuration["KIMI_API_KEY"];
var kimiTransport = builder.Configuration["KIMI_TRANSPORT"] ?? "anthropic";

if (!string.IsNullOrWhiteSpace(kimiKey) && kimiTransport.Equals("openai", StringComparison.OrdinalIgnoreCase))
{
    var kimiEndpoint = builder.Configuration["KIMI_OPENAI_ENDPOINT"] ?? "https://api.kimi.com/coding/v1";
    activeModel = builder.Configuration["KIMI_MODEL"] ?? "kimi-for-coding";
    activeProvider = "Kimi (OpenAI-compatible)";
    chatClient = new OpenAIClient(
            new ApiKeyCredential(kimiKey),
            new OpenAIClientOptions { Endpoint = new Uri(kimiEndpoint) })
        .GetChatClient(activeModel)
        .AsIChatClient();
}
else if (!string.IsNullOrWhiteSpace(kimiKey))
{
    // Anthropic-compatible endpoint: the recommended path for a coding agent on Kimi.
    var anthropicEndpoint = builder.Configuration["KIMI_ANTHROPIC_ENDPOINT"] ?? "https://api.kimi.com/coding/";
    activeModel = builder.Configuration["KIMI_MODEL"] ?? "kimi-for-coding";
    activeProvider = "Kimi (Anthropic-compatible)";
    var maxTokens = builder.Configuration.GetValue<int?>("KIMI_MAX_TOKENS") ?? 8192;
    chatClient = new AnthropicClient { ApiKey = kimiKey, BaseUrl = anthropicEndpoint }
        .AsIChatClient(activeModel, maxTokens);
}
else
{
    // Local fallback for offline testing.
    var ollamaEndpoint = builder.Configuration["OLLAMA_ENDPOINT"] ?? "http://localhost:11434";
    activeModel = builder.Configuration["OLLAMA_MODEL_NAME"] ?? "qwen3:8b";
    activeProvider = "Ollama (local)";
    chatClient = new OllamaApiClient(new Uri(ollamaEndpoint), activeModel);
}

builder.Services.AddChatClient(chatClient);

// --- Sandbox workspace (mounted into the Docker shell container; the agent only ever touches this dir) ---
var workspace = Path.Combine(builder.Environment.ContentRootPath, "workspace");
Directory.CreateDirectory(workspace);

// --- Docker-sandboxed shell tool: isolated container, workspace mounted r/w, requires approval ---
// Network "bridge" lets the agent `git clone` a repo into the workspace; set to "none" for full isolation.
// Multi-runtime sandbox image (Python + Node + .NET + git). Build it once:
//   docker build -t coding-agent-sandbox:latest -f sandbox/Dockerfile sandbox
// Override with SHELL_IMAGE to use any other image.
var shell = new DockerShellExecutor(new DockerShellExecutorOptions
{
    Image = builder.Configuration["SHELL_IMAGE"] ?? "coding-agent-sandbox:latest",
    HostWorkdir = workspace,
    ContainerWorkdir = "/workspace",
    MountReadonly = false,
    ReadOnlyRoot = false, // allow tool caches (nuget/npm/pip) and builds to write inside the container
    Network = "bridge",
    Timeout = TimeSpan.FromSeconds(120),
});
await shell.InitializeAsync();
builder.Services.AddSingleton(shell); // IAsyncDisposable: container is torn down on shutdown.

// The shell tool is approval-gated by default: the agent emits an approval request, DevUI prompts
// the user, and the approval round-trips back (the `function_approval_response` content type is now
// supported in Hosting.OpenAI). Set SHELL_REQUIRE_APPROVAL=false to auto-run (sandbox + audit hook
// still apply).
var requireShellApproval = builder.Configuration.GetValue("SHELL_REQUIRE_APPROVAL", true);
var tools = new List<AITool> { shell.AsAIFunction(name: "run_shell", requireApproval: requireShellApproval) };
tools.AddRange(FileTools.Create(workspace));

// Sub-agents as tools: specialist agents (code_reviewer, test_writer) the orchestrator can delegate to.
tools.AddRange(SubAgents.Create(chatClient));

// --- Skills: progressive-disclosure capability packs (skills/<name>/SKILL.md). The agent gets
// load_skill / read_skill_resource tools and the skill names+descriptions are advertised in context
// (DevUI Context tab). Read-only skill tools are auto-approved so loading is frictionless. ---
var skillsDir = Path.Combine(builder.Environment.ContentRootPath, "skills");
Directory.CreateDirectory(skillsDir);
var skillsProvider = new AgentSkillsProvider(skillsDir);

// --- The coding agent: system prompt + tools + skills, wrapped with the tool-audit hook ---
var agentBuilder =
    new ChatClientAgent(
        chatClient,
        new ChatClientAgentOptions
        {
            Name = "coding-agent",
            Description = "Autonomous coding agent with a Docker-sandboxed shell, workspace file tools, and skills.",
            ChatOptions = new ChatOptions
            {
                Instructions = CodingAgentInstructions.Text,
                Tools = tools,
            },
            AIContextProviders = [skillsProvider],
        })
    .AsBuilder()
    .Use(ToolAuditHook.Handler)
    .UseToolApproval(new ToolApprovalAgentOptions
    {
        // Auto-approve read-only skill tools (load_skill, read_skill_resource);
        // run_shell still requires explicit user approval.
        AutoApprovalRules = [AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule],
    });

// Emit agent/model/tool spans under the "CodingAgent" source (collected by the service defaults
// above; streamed to the console too when --instrumentation is set).
agentBuilder = agentBuilder.UseOpenTelemetry(TelemetrySource);

AIAgent codingAgent = agentBuilder.Build();

// Register the agent so the DevUI lists + runs it.
builder.Services.AddKeyedSingleton("coding-agent", codingAgent);

builder.AddDevUI();
// Emit response.trace.completed events so the DevUI Traces tab shows agent/model/tool spans.
builder.Services.AddSingleton(new InMemoryStorageOptions { EmitTraceEvents = true });
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();

app.MapOpenAIResponses();
app.MapOpenAIConversations();
app.MapDevUI();
app.MapDefaultEndpoints();

Console.WriteLine($"Model             : {activeModel} ({activeProvider})");
Console.WriteLine("Coding agent DevUI: http://localhost:5180/devui");
Console.WriteLine($"Sandbox workspace : {workspace}");
Console.WriteLine("Shell runs in a Docker container; run_shell requires approval.");

app.Run();
