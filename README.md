# GitExtensions.AICommitMessage

**Stop staring at a blank commit message.** This is a plugin for
[Git Extensions](https://github.com/gitextensions/gitextensions) that writes a first-draft commit
message for you, straight from the changes you've staged.

It adds a **`✨ AI message`** button to the Commit dialog. Click it, and the plugin sends your
**staged** diff to an AI model of your choice and drops the suggested message into the commit box —
where you read it, tweak it, and commit. No copy-pasting into a chat window, no leaving Git
Extensions.

It talks to any **OpenAI-compatible** API — OpenAI, Azure OpenAI, OpenRouter, Groq — or a model
running **locally** via [Ollama](https://ollama.com/), in which case nothing ever leaves your
machine.

<img width="820" alt="The ✨ AI message button in the Git Extensions Commit dialog" src="https://github.com/user-attachments/assets/16d7a860-c6ec-46af-964a-2bec811dc26f" />

*The **✨ AI message** button lives in the Commit dialog toolbar, right next to “Commit templates”.*

## How it feels to use

1. Stage the changes you want to commit, as usual.
2. Open the **Commit** dialog and click **`✨ AI message`**.
3. The plugin reads your staged diff and asks your model for a message. A second or two later the
   suggestion appears in the message box.
4. Edit anything you like, then commit.

Out of the box it asks for a clean [Conventional Commits](https://www.conventionalcommits.org)
subject line plus a short body explaining *why* the change was made — and you can rewrite that
instruction to match your team's style (see the **System prompt** setting below).

> **Background.** This grew out of
> [gitextensions/gitextensions#12203](https://github.com/gitextensions/gitextensions/issues/12203).
> The maintainers preferred not to bake AI into the core app and pointed to the plugin model — so
> this is a standalone plugin you opt into, nothing more.

## Privacy & safety (please read)

This plugin is built around the concerns raised on that original issue. You stay in control:

- **Off by default.** Nothing happens until you switch it on in settings *and* click the button.
- **Explicit consent, every single time.** Your diff is sent **only when you click the button** —
  never automatically, never when the dialog opens, never in the background.
- **Only *staged* content is sent.** It runs `git diff --cached`, so anything unstaged or excluded
  by `.gitignore` is never included. Stage deliberately.
- **Your key, your endpoint.** You bring your own API key — or point it at a local Ollama model so
  *nothing* leaves your machine. The key is stored in Git Extensions' plugin settings.
- **Size cap.** Large diffs are truncated to a configurable character limit, so a huge commit can't
  run up an unexpected token bill.

In short: you are sending your staged diff to whatever endpoint you configure. Don't enable it on
repositories whose contents you can't share with that provider.

## Requirements

- **Git Extensions 7.x** (built and tested against 7.0.1.86, which runs on .NET 10). The 5.2.x / .NET 8
  line is a separate, incompatible generation — see [Compatibility](#compatibility).
- **Git** available on your `PATH`.
- An API key for an OpenAI-compatible provider, **or** a local server such as Ollama.

## Compatibility

Git Extensions plugins are tied to the host's runtime **and** to the major version of its
`GitExtensions.Extensibility` contract — Git Extensions bumps that major version on every
*plugin-breaking* release, and the Plugin Manager only shows a plugin whose declared dependency range
covers the host's version. Because of that, a single build can't span generations:

| Git Extensions | Runtime | Extensibility | This plugin |
| --- | --- | --- | --- |
| **7.x** (current) | .NET 10 | `7.0.x` | **v0.2.0+** — depends on `[7.0.0, 8.0.0)` |
| 5.2.x | .NET 8 | `< 1.0` | v0.1.x (legacy, still on nuget.org) |

The `[7.0.0, 8.0.0)` range means this release works across the **entire current 7.x line** — every
patch and minor update, no re-pinning needed — but it intentionally will **not** appear in the Plugin
Manager on the older .NET 8 builds. If you're on a 5.2.x install, use the older `0.1.x` package or
update Git Extensions.

## Install

1. Build the plugin (see [Build from source](#build-from-source)) or download the release
   `.nupkg`/DLL.
2. Copy **`GitExtensions.AICommitMessage.dll`** into the Git Extensions `Plugins` folder:
   `C:\Program Files\GitExtensions\Plugins\` (writing here needs administrator rights).
3. Restart Git Extensions.

Prefer the Plugin Manager? See [Install from NuGet](#install-from-nuget-other-machines).

## Configure

Open **Settings → Plugins → AI Commit Message** and fill in the fields:

<img width="820" alt="AI commit message settings in Git Extensions" src="https://github.com/user-attachments/assets/f78f6df1-a96c-4c27-90d3-ffac81d702fc" />

| Setting | Notes |
| --- | --- |
| **Enabled** | Master switch. Off by default — turn this on first. |
| **API base URL** | `https://api.openai.com/v1` (OpenAI), `http://localhost:11434/v1` (Ollama), or any OpenAI-compatible base. |
| **Model** | e.g. `gpt-4o-mini`, `gpt-4o`, or your local model name. |
| **API key** | Masked. Leave blank for local servers that don't require auth. |
| **Max diff characters** | Truncates the diff before sending (`0` = no limit). Default `12000`. |
| **System prompt** | Steer the style. The default asks for a Conventional-Commits subject plus a body that explains *why*. |

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

## Install from NuGet (other machines)

Once published, the package depends on `GitExtensions.Extensibility` — the marker the Git Extensions
**Plugin Manager** uses to discover plugins. On another machine you can either:

- open Git Extensions → **Plugins → Plugin Manager**, find **AI Commit Message**, and install it; or
- download the `.nupkg` from [nuget.org](https://www.nuget.org/packages/GitExtensions.AICommitMessage),
  rename it to `.zip`, and copy the `lib/GitExtensions.AICommitMessage.dll` into your
  `%LOCALAPPDATA%\GitExtensions\UserPlugins\` folder.

## Releasing to NuGet

Publishing is automated by [`.github/workflows/release.yml`](.github/workflows/release.yml) using
nuget.org **Trusted Publishing** (OIDC — no stored API key to manage). One-time setup:

1. On nuget.org → **Trusted Publishing**, add a policy:
   - **Repository Owner:** `badrshs`
   - **Repository:** `GitExtensions.AICommitMessage`
   - **Workflow File:** `release.yml`
   - **Environment:** *(leave blank)*
2. In this GitHub repo, add an Actions **variable** `NUGET_USER` set to your nuget.org username
   (Settings → Secrets and variables → Actions → **Variables** → New repository variable).
3. Tag a version and push it:

   ```sh
   git tag v0.2.0
   git push origin v0.2.0
   ```

The workflow fetches the matching Git Extensions binaries, packs the plugin, obtains a short-lived
key via OIDC, and pushes to nuget.org.

To build the package locally instead:

```sh
dotnet pack src/GitExtensions.AICommitMessage/GitExtensions.AICommitMessage.csproj -c Release
# then, with your own key:
dotnet nuget push src/GitExtensions.AICommitMessage/bin/Release/GitExtensions.AICommitMessage.0.2.0.nupkg \
  -k <YOUR_NUGET_API_KEY> -s https://api.nuget.org/v3/index.json
```

## How it works

A quick tour for the curious — three small files:

- **`Plugin.cs`** exports `IGitPlugin` / `IGitPluginForCommit` via MEF. When the Commit dialog opens
  (and only if the plugin is enabled) it waits for the form to appear, then injects the
  **✨ AI message** `ToolStripButton` into the commit toolbar next to “Commit templates”. The diff
  is read and sent **only** inside the button's click handler — that's the consent boundary.
- **`GitHelper.cs`** reads the staged diff with `git --no-pager diff --cached --no-color`.
- **`OpenAiClient.cs`** POSTs the system prompt + diff to `{baseUrl}/chat/completions` and returns
  `choices[0].message.content`, which is placed into the commit message box.

## License

MIT — see [LICENSE.md](LICENSE.md).
