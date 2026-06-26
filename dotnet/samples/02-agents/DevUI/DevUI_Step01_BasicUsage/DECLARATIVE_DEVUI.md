# Definition-driven agents & workflows for the .NET DevUI

This sample extends the Agent Framework DevUI into a small **authoring + testing** surface where
agents and workflows are **data (YAML)**, reusable across flows, editable from a UI, and
**hot-reloaded from files** with no restart. It runs entirely against a **local Ollama** model
(`qwen3:8b`) — no Azure/Foundry required.

## What it demonstrates

- **Agents and workflows as YAML.** Agent definitions (`kind: Prompt`) are loaded with
  `ChatClientPromptAgentFactory.CreateFromYamlAsync`; workflow definitions (`kind: Workflow`) with
  `DeclarativeWorkflowBuilder`. Both live under `definitions/`.
- **Reuse across flows.** Workflows reference agents *by name*; one `ReviewerAgent.yaml` is used by
  both `draft-and-review` and `quick-polish`. A single name→agent registry is the reuse seam.
- **Authoring UI** at `/authoring`: an agent form (system prompt, model options), a workflow YAML
  editor, save (CRUD over `IDefinitionStore`), and inline test — alongside the standard DevUI at
  `/devui` for graphs and streaming runs.
- **File hot-reload.** A `FileSystemWatcher` rebuilds a `DefinitionCatalog` snapshot on change, and
  the framework discovers/runs entities through dynamic seams, so authored definitions appear and
  run live.

## Layout

| File | Role |
|------|------|
| `definitions/agents/*.yaml`, `definitions/workflows/*.yaml` | the definitions (data model) |
| `DefinitionStore.cs` | `IDefinitionStore` + file-backed implementation (DB-swappable) |
| `DefinitionCatalog.cs` | rebuilds agents+workflows into an atomic snapshot |
| `DefinitionWatcher.cs` | debounced file watcher → catalog reload |
| `DefinitionsApi.cs` | CRUD + on-the-fly test REST API; serves `/authoring` |
| `authoring.html` | the authoring UI (form + editor + test) |
| `LocalResponseAgentProvider.cs` | in-memory `ResponseAgentProvider` (local Ollama agents) |
| `Program.cs` | wiring: chat client, catalog, watcher, DevUI, Responses API |

## Two framework seams (this branch patches them)

Hot-reload requires the framework to read entities dynamically rather than only from startup DI:

- `Microsoft.Agents.AI.DevUI/EntitiesApiExtensions.cs` — discovery also consults registered
  `Func<IEnumerable<T>>` sources (per request).
- `Microsoft.Agents.AI.Hosting.OpenAI/.../HostedAgentResponseExecutor.cs` — agent resolution falls
  back to an optional `Func<string, AIAgent?>` resolver.

Both changes are additive and backward-compatible (no-ops when nothing is registered) — candidates
to upstream.

## Run it

Prerequisites: .NET 10 SDK and a local [Ollama](https://ollama.com) with `qwen3:8b` pulled
(`ollama pull qwen3:8b`).

```bash
cd dotnet/samples/02-agents/DevUI/DevUI_Step01_BasicUsage
dotnet run
```

- DevUI: http://localhost:50518/devui
- Authoring: http://localhost:50518/authoring

Override the model with `OLLAMA_ENDPOINT` / `OLLAMA_MODEL_NAME`.

## Known limitations

- The workflow test output can be duplicated (workflow output + agent message both surface).
- Tool-using agent definitions (`tools:` bindings) are not yet wired in this sample.
- The authoring UI is a companion page, not (yet) integrated into the DevUI React app.
