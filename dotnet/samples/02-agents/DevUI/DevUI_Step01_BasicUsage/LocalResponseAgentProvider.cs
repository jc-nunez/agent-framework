// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.AI;

namespace DevUI_Step01_BasicUsage;

/// <summary>
/// A minimal, fully local <see cref="ResponseAgentProvider"/> for declarative workflows.
/// </summary>
/// <remarks>
/// The declarative workflow engine resolves and invokes agents through a <see cref="ResponseAgentProvider"/>.
/// The only shipped implementation (<c>AzureAgentProvider</c>) talks to Foundry. This implementation instead
/// resolves agents from an in-memory registry and keeps conversation state in memory, so
/// <c>InvokeAzureAgent</c> actions execute against local agents (e.g. Ollama) with no Azure dependency.
///
/// Local chat clients have no server-side conversation store, so this provider treats its in-memory
/// conversation as the source of truth: each invocation runs the agent over the full history and appends
/// the agent's reply so the next action/agent sees it.
/// </remarks>
internal sealed class LocalResponseAgentProvider(IReadOnlyDictionary<string, AIAgent> agents) : ResponseAgentProvider
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    /// <inheritdoc/>
    public override Task<string> CreateConversationAsync(CancellationToken cancellationToken = default)
    {
        string conversationId = $"conv_{Guid.NewGuid():N}";
        this._conversations[conversationId] = [];
        return Task.FromResult(conversationId);
    }

    /// <inheritdoc/>
    public override Task<ChatMessage> CreateMessageAsync(string conversationId, ChatMessage conversationMessage, CancellationToken cancellationToken = default)
    {
        conversationMessage.MessageId ??= NewMessageId();
        this.GetHistory(conversationId).Add(conversationMessage);
        return Task.FromResult(conversationMessage);
    }

    /// <inheritdoc/>
    public override Task<ChatMessage> GetMessageAsync(string conversationId, string messageId, CancellationToken cancellationToken = default)
    {
        ChatMessage message =
            this.GetHistory(conversationId).FirstOrDefault(m => m.MessageId == messageId)
            ?? throw new InvalidOperationException($"Message '{messageId}' not found in conversation '{conversationId}'.");
        return Task.FromResult(message);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<AgentResponseUpdate> InvokeAgentAsync(
        string agentId,
        string? agentVersion,
        string? conversationId,
        IEnumerable<ChatMessage>? messages,
        IDictionary<string, object?>? inputArguments,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!agents.TryGetValue(agentId, out AIAgent? agent))
        {
            throw new InvalidOperationException(
                $"Agent '{agentId}' is not registered. Known agents: {string.Join(", ", agents.Keys)}.");
        }

        List<ChatMessage> history =
            this.GetHistory(conversationId ?? throw new InvalidOperationException("A conversationId is required to invoke an agent."));

        // Messages supplied directly by the action are new inputs — record them before invoking.
        if (messages is not null)
        {
            foreach (ChatMessage message in messages)
            {
                message.MessageId ??= NewMessageId();
                history.Add(message);
            }
        }

        // Local clients have no server-side conversation, so pass the full history explicitly.
        ChatMessage[] input = [.. history];
        ChatClientAgentRunOptions runOptions = new(new ChatOptions { AllowMultipleToolCalls = this.AllowMultipleToolCalls });

        StringBuilder replyText = new();
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(input, null, runOptions, cancellationToken).ConfigureAwait(false))
        {
            update.AuthorName = agentId;
            replyText.Append(update.Text);
            yield return update;
        }

        // Append the agent's reply so subsequent actions/agents observe it in the shared conversation.
        history.Add(new ChatMessage(ChatRole.Assistant, replyText.ToString())
        {
            AuthorName = agentId,
            MessageId = NewMessageId(),
        });
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatMessage> GetMessagesAsync(
        string conversationId,
        int? limit = null,
        string? after = null,
        string? before = null,
        bool newestFirst = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IEnumerable<ChatMessage> history = this.GetHistory(conversationId);
        if (newestFirst)
        {
            history = history.AsEnumerable().Reverse();
        }

        if (limit is int max)
        {
            history = history.Take(max);
        }

        foreach (ChatMessage message in history)
        {
            yield return message;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private List<ChatMessage> GetHistory(string conversationId) =>
        this._conversations.TryGetValue(conversationId, out List<ChatMessage>? history)
            ? history
            : throw new InvalidOperationException($"Conversation '{conversationId}' does not exist.");

    private static string NewMessageId() => $"msg_{Guid.NewGuid():N}";
}
