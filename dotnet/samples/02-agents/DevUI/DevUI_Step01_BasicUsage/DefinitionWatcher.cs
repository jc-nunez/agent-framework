// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Logging;

namespace DevUI_Step01_BasicUsage;

/// <summary>
/// Watches the definitions directory and reloads the <see cref="DefinitionCatalog"/> when files
/// change, so authoring (via the form or editing YAML directly) is reflected without a restart.
/// </summary>
/// <remarks>
/// File systems emit bursts of events for a single save, so changes are debounced before a reload.
/// Reloads are serialized to avoid overlapping rebuilds.
/// </remarks>
internal sealed class DefinitionWatcher : IDisposable
{
    private readonly DefinitionCatalog _catalog;
    private readonly ILogger<DefinitionWatcher> _logger;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    public DefinitionWatcher(string definitionsDirectory, DefinitionCatalog catalog, ILogger<DefinitionWatcher> logger)
    {
        this._catalog = catalog;
        this._logger = logger;

        Directory.CreateDirectory(definitionsDirectory);
        this._debounce = new Timer(_ => _ = this.ReloadAsync(), state: null, Timeout.Infinite, Timeout.Infinite);

        this._watcher = new FileSystemWatcher(definitionsDirectory, "*.yaml")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        this._watcher.Created += this.OnChanged;
        this._watcher.Changed += this.OnChanged;
        this._watcher.Deleted += this.OnChanged;
        this._watcher.Renamed += this.OnChanged;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
        // Coalesce the burst of events from a single save into one reload ~400ms later.
        => this._debounce.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);

    private async Task ReloadAsync()
    {
        await this._reloadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await this._catalog.ReloadAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to reload definitions after a file change.");
        }
        finally
        {
            this._reloadLock.Release();
        }
    }

    public void Dispose()
    {
        this._watcher.Dispose();
        this._debounce.Dispose();
        this._reloadLock.Dispose();
    }
}
