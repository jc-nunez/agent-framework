// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CodingAgent.Web;

/// <summary>
/// Specialist sub-agents exposed to the main coding agent as tools. Each is a focused
/// <see cref="ChatClientAgent"/> turned into an <see cref="AIFunction"/> via <c>AsAIFunction()</c> —
/// so the orchestrator can "call" a sub-agent the same way it calls any other tool. The function name
/// and description come from the sub-agent's Name/Description, which is how the model decides to delegate.
/// </summary>
internal static class SubAgents
{
    public static IEnumerable<AITool> Create(IChatClient chatClient)
    {
        var codeReviewer = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = "code_reviewer",
                Description = "Delegate a focused code review. Pass the code snippet or diff (and what changed); "
                    + "returns concise findings on correctness, tests, security, error handling, and style.",
                ChatOptions = new ChatOptions
                {
                    Instructions =
                        """
                        You are a senior code reviewer. Review the provided code or diff for correctness,
                        missing tests, security issues, error handling, and consistency. Return a concise list
                        of findings, each as 'severity: issue — suggested fix'. If it looks good, say so briefly.
                        Point to specific problems; do not rewrite the whole file.
                        """,
                },
            });

        var testWriter = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = "test_writer",
                Description = "Delegate writing unit tests. Pass the function/class/file and the language; "
                    + "returns runnable tests covering the happy path and key edge cases.",
                ChatOptions = new ChatOptions
                {
                    Instructions =
                        """
                        You are a test engineer. Given a function, class, or file, write focused, runnable unit
                        tests covering the happy path and important edge cases (null/empty, boundaries, errors).
                        Use the idiomatic framework for the language (pytest for Python, xUnit for C#, node:test
                        for Node). Output the test code in a single code block, then a one-line note on how to run it.
                        """,
                },
            });

        return [codeReviewer.AsAIFunction(), testWriter.AsAIFunction()];
    }
}
