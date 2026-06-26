using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;
using GitExtensions.Extensibility.Settings;
using GitUIPluginInterfaces;

namespace GitExtensions.AICommitMessage
{
    /// <summary>
    /// Adds a "Generate AI commit message" button to the commit dialog. When clicked, the staged diff
    /// is sent to a user-configured OpenAI-compatible endpoint and the returned message is inserted.
    /// The feature is OFF by default and the diff leaves the machine only on an explicit click.
    /// </summary>
    [Export(typeof(IGitPlugin))]
    [Export(typeof(IGitPluginForCommit))]
    [Export(typeof(IGitPluginForRepository))]
    public sealed class Plugin : GitPluginBase, IGitPluginForRepository, IGitPluginForCommit
    {
        private const string TemplateTitle = "✨ Generate AI commit message";

        private const string DefaultSystemPrompt =
            "You are a senior software engineer writing a git commit message for a staged diff. " +
            "Produce a Conventional Commits style message: a concise imperative subject line (<= 72 chars), " +
            "then a blank line, then a short body that explains WHAT changed and, more importantly, WHY. " +
            "Infer intent from the diff; do not invent facts. " +
            "Output only the raw commit message text — no markdown code fences, no surrounding quotes, no commentary.";

        private readonly BoolSetting _enabled = new("Enabled", "Enable AI commit message generation", false);
        private readonly StringSetting _baseUrl = new("API base URL", "API base URL (OpenAI-compatible, e.g. https://api.openai.com/v1 or http://localhost:11434/v1)", "https://api.openai.com/v1");
        private readonly StringSetting _model = new("Model", "Model", "gpt-4o-mini");
        private readonly PasswordSetting _apiKey = new("API key", "API key (leave blank for local servers such as Ollama)", "");
        private readonly StringSetting _systemPrompt = new("System prompt", "System prompt", DefaultSystemPrompt, useDefaultValueIfBlank: true);
        private readonly NumberSetting<int> _maxDiffChars = new("Max diff characters", "Max diff characters sent to the model (0 = no limit)", 12000);

        private IGitModule? _module;
        private bool _templateAdded;

        public Plugin()
            : base(hasSettings: true)
        {
            Name = "AI Commit Message";
            Description = "Generate a commit message from the staged diff via an OpenAI-compatible API";
            Icon = LoadIcon();
        }

        public override IEnumerable<ISetting> GetSettings()
        {
            yield return _enabled;
            yield return _baseUrl;
            yield return _model;
            yield return _apiKey;
            yield return _maxDiffChars;
            yield return _systemPrompt;
        }

        public override void Register(IGitUICommands gitUiCommands)
        {
            base.Register(gitUiCommands);
            _module = gitUiCommands.Module;
            gitUiCommands.PreCommit += OnPreCommit;
            gitUiCommands.PostCommit += OnPostRepositoryChanged;
            gitUiCommands.PostRepositoryChanged += OnPostRepositoryChanged;
        }

        public override void Unregister(IGitUICommands gitUiCommands)
        {
            gitUiCommands.PreCommit -= OnPreCommit;
            gitUiCommands.PostCommit -= OnPostRepositoryChanged;
            gitUiCommands.PostRepositoryChanged -= OnPostRepositoryChanged;
            base.Unregister(gitUiCommands);
        }

        /// <summary>Invoked from the Plugins menu — just open the settings page.</summary>
        public override bool Execute(GitUIEventArgs args)
        {
            args.GitUICommands.StartSettingsDialog(this);
            return false;
        }

        private void OnPreCommit(object? sender, GitUIEventArgs e)
        {
            if (!_enabled.ValueOrDefault(Settings))
            {
                return;
            }

            // The Func<string> runs only when the user clicks the button in the commit dialog,
            // so nothing is sent to the AI until that explicit action.
            e.GitUICommands.AddCommitTemplate(TemplateTitle, GenerateCommitMessage, Icon);
            _templateAdded = true;
        }

        private void OnPostRepositoryChanged(object? sender, GitUIEventArgs e)
        {
            if (_templateAdded)
            {
                e.GitUICommands.RemoveCommitTemplate(TemplateTitle);
                _templateAdded = false;
            }
        }

        private string GenerateCommitMessage()
        {
            Cursor? previous = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;

                string? workingDir = _module?.WorkingDir;
                if (string.IsNullOrEmpty(workingDir))
                {
                    return string.Empty;
                }

                string baseUrl = _baseUrl.ValueOrDefault(Settings);
                string model = _model.ValueOrDefault(Settings);
                string apiKey = _apiKey.ValueOrDefault(Settings);
                string systemPrompt = _systemPrompt.ValueOrDefault(Settings);
                if (string.IsNullOrWhiteSpace(systemPrompt))
                {
                    systemPrompt = DefaultSystemPrompt;
                }

                int maxChars = _maxDiffChars.ValueOrDefault(Settings);

                // Run off the UI thread; ConfigureAwait(false) throughout avoids a UI-thread deadlock.
                return Task.Run(() => GenerateAsync(workingDir!, baseUrl, apiKey, model, systemPrompt, maxChars))
                           .GetAwaiter().GetResult();
            }
            catch (NoStagedChangesException)
            {
                MessageBox.Show(
                    "No staged changes were found. Stage the files you want to commit, then click the button again.",
                    TemplateTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return string.Empty;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to generate a commit message:\n\n" + ex.Message,
                    TemplateTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return string.Empty;
            }
            finally
            {
                Cursor.Current = previous;
            }
        }

        private static async Task<string> GenerateAsync(string workingDir, string baseUrl, string apiKey, string model, string systemPrompt, int maxChars)
        {
            string diff = GitHelper.GetStagedDiff(workingDir);
            if (string.IsNullOrWhiteSpace(diff))
            {
                throw new NoStagedChangesException();
            }

            if (maxChars > 0 && diff.Length > maxChars)
            {
                diff = diff.Substring(0, maxChars) + "\n\n[diff truncated to fit the configured limit]";
            }

            OpenAiClient client = new(baseUrl, apiKey, model);
            return await client.CompleteAsync(systemPrompt, diff).ConfigureAwait(false);
        }

        private static Image? LoadIcon()
        {
            try
            {
                using System.IO.Stream? stream = typeof(Plugin).Assembly
                    .GetManifestResourceStream("GitExtensions.AICommitMessage.Resources.icon.png");
                return stream is null ? null : Image.FromStream(stream);
            }
            catch
            {
                return null;
            }
        }

        private sealed class NoStagedChangesException : Exception
        {
        }
    }
}
