// Copyright (c) Microsoft. All rights reserved.

// Aspire orchestrator for the coding agent. Runs the CodingAgent.Web service (which embeds the DevUI)
// and wires the Aspire dashboard, so the OpenTelemetry traces/metrics/logs the agent emits — agent
// runs, tool calls, model requests — are visible in the dashboard. Run with: `dotnet run` (or `aspire run`).
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.CodingAgent_Web>("coding-agent");

builder.Build().Run();
