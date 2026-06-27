// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CodingAgent.Web;

/// <summary>
/// A function-invocation HOOK: wraps every tool call the agent makes (pre + post), so you can audit,
/// time, redact, block, or transform tool usage in one place. Attached via
/// <c>agent.AsBuilder().Use(ToolAuditHook.Handler)</c>.
/// </summary>
internal static class ToolAuditHook
{
    public static async ValueTask<object?> Handler(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        var name = context.Function.Name;
        var args = string.Join(", ", context.Arguments.Select(a => $"{a.Key}={Preview(a.Value)}"));

        // PRE-hook: a real implementation could enforce policy here (e.g. block writes outside the
        // workspace, deny network-touching commands, require approval) by throwing or returning early.
        Console.WriteLine($"[hook] → {name}({args})");

        var start = System.Environment.TickCount64;
        try
        {
            var result = await next(context, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"[hook] ← {name} ok ({System.Environment.TickCount64 - start}ms)");
            return result;
        }
        catch (Exception ex)
        {
            // POST-hook (failure): surface tool failures in the audit trail.
            Console.WriteLine($"[hook] ✗ {name} failed: {ex.Message}");
            throw;
        }
    }

    private static string Preview(object? value)
    {
        var s = value?.ToString() ?? "null";
        return s.Length <= 80 ? s : s[..77] + "...";
    }
}
