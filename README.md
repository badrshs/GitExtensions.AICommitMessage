# GitExtensions.AICommitMessage

A [Git Extensions](https://github.com/gitextensions/gitextensions) plugin that generates a commit
message from your **staged** changes using any **OpenAI-compatible** API (OpenAI, Azure OpenAI,
OpenRouter, Groq, or a local model via [Ollama](https://ollama.com/)).

It adds a **`✨ Generate AI commit message`** button to the commit dialog. Click it and the staged
diff is sent to the model you configured; the suggested message is inserted into the message box,
where you can edit it before committing.

> Background: this implements the idea from
> [gitextensions/gitextensions#12203](https://github.com/gitextensions/gitextensions/issues/12203).
> The maintainers declined to put AI in the core app but pointed to the plugin model — so this is a
> standalone plugin built on the same `AddCommitTemplate` hook the bundled Azure DevOps plugin uses.

## Privacy & safety (read this)

This plugin is designed around the concerns raised on the original issue:

- **Off by default.** Nothing happens until you enable it in settings *and* click the button.
- **Explicit consent every time.** The staged diff is sent **only when you click the button** — never
  automatically, never in the background.
- **Only staged content is sent.** It runs `git diff --cached`, so files excluded by `.gitignore`
  are never included. Stage deliberately.
- **Your key, your endpoint.** You supply your own API key (or point it at a local model, so nothing
  leaves your machine at all). The key is stored in Git Extensions' plugin settings.
- **Size cap.** Large diffs are truncated to a configurable character limit to control cost/tokens.

You are sending your staged diff to whatever endpoint you configure. Don't enable it on repositories
whose contents you can't share with that provider.

## Requirements

- **Git Extensions 5.2.x** (built and tested against 5.2.1, which runs on .NET 8).
- **Git** on your `PATH`.
- An API key for an OpenAI-compatible provider, **or** a local server such as Ollama.

## Install

1. Build the plugin (see below) or download the release `.nupkg`/DLL.
2. Copy **`GitExtensions.AICommitMessage.dll`** into the Git Extensions `Plugins` folder:
   `C:\Program Files\GitExtensions\Plugins\` (writing here needs administrator rights).
3. Restart Git Extensions.

## Configure

Open **Settings → Plugins → AI Commit Message**:

| Setting | Notes |
| --- | --- |
| **Enabled** | Master switch. Off by default. |
| **API base URL** | `https://api.openai.com/v1` (OpenAI), `http://localhost:11434/v1` (Ollama), or any OpenAI-compatible base. |
| **Model** | e.g. `gpt-4o-mini`, `gpt-4o`, or your local model name. |
| **API key** | Masked. Leave blank for local servers that don't require auth. |
| **Max diff characters** | Truncates the diff before sending (`0` = no limit). Default `12000`. |
| **System prompt** | Steer the style. The default asks for a Conventional-Commits subject plus a body explaining *why*. |

## Use

1. Stage the changes you want to commit.
2. Open the **Commit** dialog.
3. Click **`✨ Generate AI commit message`**.
4. Review and edit the suggestion, then commit.

## Build from source

```sh
dotnet build src/GitExtensions.AICommitMessage/GitExtensions.AICommitMessage.csproj -c Release
```

The project compiles against the Git Extensions assemblies in your install
(`C:\Program Files\GitExtensions` by default). If yours is elsewhere:

```sh
dotnet build ... -c Release -p:GitExtensionsPath="D:\path\to\GitExtensions"
```

Those host assemblies are referenced with `Private=false`, so the build output is just the plugin
DLL — Git Extensions provides the rest at runtime.

## Package & publish

```sh
dotnet pack src/GitExtensions.AICommitMessage/GitExtensions.AICommitMessage.csproj -c Release
```

This produces a `.nupkg` that depends on `GitExtensions.Extensibility` — the marker the Git Extensions
**Plugin Manager** uses to discover plugins. Publish it to [nuget.org](https://www.nuget.org/) to make
it installable from the Plugin Manager.

## How it works

- `Plugin.cs` exports `IGitPlugin` / `IGitPluginForCommit` via MEF, and on `PreCommit` registers a
  commit template via `IGitUICommands.AddCommitTemplate(title, Func<string>, icon)`. The `Func<string>`
  runs only on click — that's the consent boundary.
- `GitHelper.cs` reads the staged diff with `git diff --cached`.
- `OpenAiClient.cs` calls `{baseUrl}/chat/completions` and returns `choices[0].message.content`.

## License

MIT — see [LICENSE.md](LICENSE.md).
