// Copyright (c) Microsoft. All rights reserved.

using System.ClientModel;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OpenAI;

namespace CodingAgent.Evals;

/// <summary>
/// Builds the chat clients to compare in the eval — the same provider matrix as the Web app, so the
/// eval scores exactly what the agent would run. With a Kimi key set, this A/Bs the Anthropic and
/// OpenAI transports against the local Ollama baseline.
/// </summary>
internal static class Providers
{
    public static List<(string Name, IChatClient Client)> ForComparison(IConfiguration config)
    {
        var providers = new List<(string, IChatClient)>();

        var kimiKey = config["KIMI_API_KEY"];
        var model = config["KIMI_MODEL"] ?? "kimi-for-coding";
        if (!string.IsNullOrWhiteSpace(kimiKey))
        {
            providers.Add(("kimi-anthropic", new AnthropicClient
            {
                ApiKey = kimiKey,
                BaseUrl = config["KIMI_ANTHROPIC_ENDPOINT"] ?? "https://api.kimi.com/coding/",
            }.AsIChatClient(model, 8192)));

            providers.Add(("kimi-openai", new OpenAIClient(
                    new ApiKeyCredential(kimiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(config["KIMI_OPENAI_ENDPOINT"] ?? "https://api.kimi.com/coding/v1") })
                .GetChatClient(model)
                .AsIChatClient()));
        }

        var ollamaModel = config["OLLAMA_MODEL_NAME"] ?? "qwen3:8b";
        providers.Add(($"ollama:{ollamaModel}", new OllamaApiClient(
            new Uri(config["OLLAMA_ENDPOINT"] ?? "http://localhost:11434"), ollamaModel)));

        return providers;
    }

    /// <summary>Picks the judge model: prefer Kimi (Anthropic) when available, else the first provider.</summary>
    public static IChatClient Judge(List<(string Name, IChatClient Client)> providers)
    {
        foreach (var (name, client) in providers)
        {
            if (name.StartsWith("kimi", StringComparison.Ordinal))
            {
                return client;
            }
        }

        return providers[0].Client;
    }
}
