// Copyright (c) Microsoft. All rights reserved.

// Eval/scorer harness for the coding agent. Runs a small suite of coding prompts through each
// available provider and scores the responses with LLM-judged quality evaluators
// (Relevance, Coherence, Fluency) from Microsoft.Extensions.AI.Evaluation.Quality.
//
// Its main purpose: turn "which model/transport is better for coding?" into numbers — it A/Bs the
// Kimi Anthropic vs OpenAI transports (and the Ollama baseline) on identical prompts and a shared judge.
//
//   dotnet user-secrets set "KIMI_API_KEY" "<key>"   # (shared with CodingAgent.Web)
//   dotnet run

using CodingAgent.Evals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var providers = Providers.ForComparison(config);
var chatConfiguration = new ChatConfiguration(Providers.Judge(providers));

IEvaluator[] evaluators =
[
    new RelevanceEvaluator(),
    new CoherenceEvaluator(),
    new FluencyEvaluator(),
];

string[] prompts =
[
    "Write a Python function `fib(n)` that returns the nth Fibonacci number iteratively. Include a docstring.",
    "In C#, this throws NullReferenceException: `string s = null; Console.WriteLine(s.Length);`. Explain why and give a corrected version.",
    "Given a list of integers and a target, return the indices of the two numbers that add up to the target (two-sum). Provide a Python solution and its time complexity.",
];

Console.WriteLine($"Judge model: {chatConfiguration.ChatClient.GetService<ChatClientMetadata>()?.DefaultModelId ?? "(unknown)"}");
Console.WriteLine($"Providers : {string.Join(", ", providers.Select(p => p.Name))}");
Console.WriteLine($"Prompts   : {prompts.Length} | Evaluators: {string.Join(", ", evaluators.Select(e => e.GetType().Name))}");
Console.WriteLine(new string('=', 72));

// provider -> metric -> running total + count
var totals = new Dictionary<string, Dictionary<string, (double Sum, int Count)>>();

foreach (var (providerName, client) in providers)
{
    totals[providerName] = [];
    Console.WriteLine($"\n### {providerName}");

    foreach (var prompt in prompts)
    {
        var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };
        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(messages);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ! response failed: {ex.Message}");
            continue;
        }

        foreach (var evaluator in evaluators)
        {
            try
            {
                EvaluationResult result = await evaluator.EvaluateAsync(messages, response, chatConfiguration);
                foreach (var metric in result.Metrics.Values)
                {
                    if (metric is NumericMetric { Value: { } value })
                    {
                        var bucket = totals[providerName].GetValueOrDefault(metric.Name);
                        totals[providerName][metric.Name] = (bucket.Sum + value, bucket.Count + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ! {evaluator.GetType().Name} failed: {ex.Message}");
            }
        }

        Console.Write(".");
    }

    Console.WriteLine();
}

// --- Scoreboard (average score per provider per metric, 1-5) ---
Console.WriteLine("\n" + new string('=', 72));
Console.WriteLine("SCOREBOARD (avg 1-5)");
var metricNames = totals.Values.SelectMany(m => m.Keys).Distinct().Order().ToList();
Console.WriteLine($"{"provider",-22} {string.Join(" ", metricNames.Select(n => n.PadLeft(12)))}  {"overall",12}");
foreach (var (providerName, metrics) in totals)
{
    var cells = metricNames.Select(n =>
        metrics.TryGetValue(n, out var b) && b.Count > 0 ? (b.Sum / b.Count).ToString("0.00").PadLeft(12) : "n/a".PadLeft(12));
    double overallSum = 0;
    int overallCount = 0;
    foreach (var b in metrics.Values)
    {
        overallSum += b.Sum;
        overallCount += b.Count;
    }

    var overall = overallCount > 0 ? (overallSum / overallCount).ToString("0.00") : "n/a";
    Console.WriteLine($"{providerName,-22} {string.Join(" ", cells)}  {overall,12}");
}
