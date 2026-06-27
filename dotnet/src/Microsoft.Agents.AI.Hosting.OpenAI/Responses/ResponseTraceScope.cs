// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;

/// <summary>
/// Captures the OpenTelemetry <see cref="Activity"/> instances produced while a single response
/// executes, so they can be emitted as <c>response.trace.completed</c> events for the DevUI Traces tab.
/// </summary>
/// <remarks>
/// A process-wide <see cref="ActivityListener"/> is registered lazily the first time a scope is created
/// (i.e. only when <see cref="InMemoryStorageOptions.EmitTraceEvents"/> is enabled). Completed activities
/// are attributed to the current scope via an <see cref="AsyncLocal{T}"/>, so concurrent responses do not
/// mix spans.
/// </remarks>
internal sealed class ResponseTraceScope : IDisposable
{
    private static readonly AsyncLocal<List<Activity>?> s_current = new();
    private static int s_listenerInitialized;

    private readonly List<Activity> _activities = [];

    public ResponseTraceScope()
    {
        EnsureListener();
        s_current.Value = this._activities;
    }

    /// <summary>The activities (spans) collected during this scope.</summary>
    public IReadOnlyList<Activity> Activities => this._activities;

    public void Dispose() => s_current.Value = null;

    /// <summary>Projects a completed <see cref="Activity"/> into a <see cref="TraceSpanData"/>.</summary>
    public static TraceSpanData ToSpanData(Activity activity)
    {
        var parentSpanId = activity.ParentSpanId.ToHexString();

        Dictionary<string, string>? attributes = null;
        foreach (var tag in activity.TagObjects)
        {
            attributes ??= [];
            attributes[tag.Key] = tag.Value?.ToString() ?? string.Empty;
        }

        return new TraceSpanData
        {
            SpanId = activity.SpanId.ToHexString(),
            TraceId = activity.TraceId.ToHexString(),
            ParentSpanId = parentSpanId is "0000000000000000" ? null : parentSpanId,
            OperationName = string.IsNullOrEmpty(activity.DisplayName) ? activity.OperationName : activity.DisplayName,
            DurationMs = activity.Duration.TotalMilliseconds,
            Status = activity.Status.ToString(),
            Attributes = attributes,
            Timestamp = activity.StartTimeUtc.ToString("o", CultureInfo.InvariantCulture),
        };
    }

    private static void EnsureListener()
    {
        if (Interlocked.Exchange(ref s_listenerInitialized, 1) == 1)
        {
            return;
        }

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStopped = static activity => s_current.Value?.Add(activity),
        });
    }
}
