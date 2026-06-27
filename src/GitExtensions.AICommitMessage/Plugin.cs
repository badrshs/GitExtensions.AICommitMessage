using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;
using GitExtensions.Extensibility.Settings;
using GitUIPluginInterfaces;

namespace GitExtensions.AICommitMessage
{
    /// <summary>
    /// Adds an "AI message" button to the Commit dialog toolbar (next to "Commit templates").
    /// Clicking it sends the staged diff to a user-configured OpenAI-compatible endpoint and puts the
    /// suggested message in the commit box. The feature is OFF by default and the diff is sent ONLY
    /// on an explicit click — never when the dialog opens or a menu is browsed.
    /// </summary>
    [Export(typeof(IGitPlugin))]
    [Export(typeof(IGitPluginForCommit))]
    [Export(typeof(IGitPluginForRepository))]
    public sealed class Plugin : GitPluginBase, IGitPluginForRepository, IGitPluginForCommit
    {
        private const string Title = "AI commit message";
        private const string ButtonName = "aiCommitMessageButton";
        private const string ButtonText = "✨ AI message";

        private const string DefaultSystemPrompt =
            "You are a senior software engineer writing a git commit message for the staged diff.\n" +
            "\n" +
            "Write the message in this exact shape:\n" +
            "\n" +
            "1. Subject line: imperative mood, at most 50 characters, no trailing period.\n" +
            "   Use a Conventional Commits prefix when it fits the change, one of:\n" +
            "   feat, fix, refactor, docs, test, chore, perf, build, ci\n" +
            "   (example: \"fix: prevent crash when staging an empty file\").\n" +
            "2. Then exactly one blank line.\n" +
            "3. Body: explain WHAT changed and, above all, WHY it changed. Hard-wrap\n" +
            "   every body line at 72 characters. Use \"- \" bullet points when there\n" +
            "   are several distinct changes.\n" +
            "\n" +
            "Guidelines:\n" +
            "- Infer the intent from the diff; never invent changes that aren't there.\n" +
            "- For a small, self-explanatory change, a subject line alone is fine.\n" +
            "- Be concise and specific; avoid filler like \"updated some code\".\n" +
            "\n" +
            "Output ONLY the raw commit message text: no markdown, no code fences, no\n" +
            "surrounding quotes, and no commentary before or after it.";

        private readonly BoolSetting _enabled = new("Enabled", "Enable AI commit message generation", false);
        private readonly StringSetting _baseUrl = new("API base URL", "API base URL (OpenAI-compatible, e.g. https://api.openai.com/v1 or http://localhost:11434/v1)", "https://api.openai.com/v1");
        private readonly StringSetting _model = new("Model", "Model", "gpt-4o-mini");
        private readonly PasswordSetting _apiKey = new("API key", "API key (leave blank for local servers such as Ollama)", "");
        private readonly StringSetting _systemPrompt = new("System prompt", "System prompt", DefaultSystemPrompt, useDefaultValueIfBlank: true);
        private readonly NumberSetting<int> _maxDiffChars = new("Max diff characters", "Max diff characters sent to the model (0 = no limit)", 12000);

        private IGitModule? _module;
        private bool _idleHooked;

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

            // Show the system prompt in a tall, multi-line box so it's readable and editable.
            _systemPrompt.CustomControl = new TextBox
            {
                Multiline = true,
                Height = 160,
                WordWrap = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical
            };
            yield return _systemPrompt;
        }

        public override void Register(IGitUICommands gitUiCommands)
        {
            base.Register(gitUiCommands);
            _module = gitUiCommands.Module;
            gitUiCommands.PreCommit += OnPreCommit;
            gitUiCommands.PostCommit += OnPostCommit;
        }

        public override void Unregister(IGitUICommands gitUiCommands)
        {
            gitUiCommands.PreCommit -= OnPreCommit;
            gitUiCommands.PostCommit -= OnPostCommit;
            UnhookIdle();
            base.Unregister(gitUiCommands);
        }

        /// <summary>Plugins menu entry — just open the settings page.</summary>
        public override bool Execute(GitUIEventArgs args)
        {
            args.GitUICommands.StartSettingsDialog(this);
            return false;
        }

        // PreCommit fires right before the Commit dialog is created. We can't touch the form yet, so we
        // wait for the app to go idle (the form is shown by then) and inject the button into its toolbar.
        private void OnPreCommit(object? sender, GitUIEventArgs e)
        {
            if (!_enabled.ValueOrDefault(Settings))
            {
                return;
            }

            HookIdle();
        }

        private void OnPostCommit(object? sender, GitUIPostActionEventArgs e) => UnhookIdle();

        private void HookIdle()
        {
            if (_idleHooked)
            {
                return;
            }

            _idleHooked = true;
            Application.Idle += OnApplicationIdle;
        }

        private void UnhookIdle()
        {
            if (!_idleHooked)
            {
                return;
            }

            _idleHooked = false;
            Application.Idle -= OnApplicationIdle;
        }

        private void OnApplicationIdle(object? sender, EventArgs e)
        {
            Form? form = FindOpenForm("FormCommit");
            if (form is null)
            {
                return; // dialog not visible yet — try again on the next idle
            }

            UnhookIdle();
            try
            {
                InjectButton(form);
            }
            catch
            {
                // Best effort: if the toolbar layout changed in this GE build, just skip the button.
            }
        }

        private void InjectButton(Form form)
        {
            ToolStripItem? templatesItem = GetMember(form, "commitTemplatesToolStripMenuItem") as ToolStripItem;
            ToolStrip? host = GetMember(form, "toolbarCommit") as ToolStrip;

            int insertIndex = -1;
            if (host is not null && templatesItem is not null)
            {
                int idx = host.Items.IndexOf(templatesItem);
                if (idx >= 0)
                {
                    insertIndex = idx + 1;
                }
            }

            host ??= AllControls(form).OfType<ToolStrip>()
                        .FirstOrDefault(ts => templatesItem is not null && ts.Items.Contains(templatesItem))
                     ?? AllControls(form).OfType<ToolStrip>().FirstOrDefault();

            if (host is null)
            {
                return;
            }

            // Avoid adding a second button if this form was already processed.
            if (host.Items.Cast<ToolStripItem>().Any(i => i.Name == ButtonName))
            {
                return;
            }

            ToolStripButton button = new()
            {
                Name = ButtonName,
                Text = ButtonText,
                Image = Icon,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                ToolTipText = "Generate a commit message from the staged diff"
            };
            button.Click += async (_, _) => await OnGenerateClickedAsync(form, button).ConfigureAwait(true);

            if (insertIndex >= 0 && insertIndex <= host.Items.Count)
            {
                host.Items.Insert(insertIndex, button);
            }
            else
            {
                host.Items.Add(button);
            }
        }

        private async Task OnGenerateClickedAsync(Form form, ToolStripButton button)
        {
            string? workingDir = _module?.WorkingDir;
            if (string.IsNullOrEmpty(workingDir))
            {
                return;
            }

            // Read settings on the UI thread.
            string baseUrl = _baseUrl.ValueOrDefault(Settings);
            string model = _model.ValueOrDefault(Settings);
            string apiKey = _apiKey.ValueOrDefault(Settings);
            string systemPrompt = _systemPrompt.ValueOrDefault(Settings);
            if (string.IsNullOrWhiteSpace(systemPrompt))
            {
                systemPrompt = DefaultSystemPrompt;
            }

            int maxChars = _maxDiffChars.ValueOrDefault(Settings);

            string? originalText = button.Text;
            button.Enabled = false;
            button.Text = "Generating…";
            try
            {
                // Off the UI thread; the continuation resumes on the UI thread to update the form.
                string message = await Task.Run(() => GenerateAsync(workingDir!, baseUrl, apiKey, model, systemPrompt, maxChars));
                if (!string.IsNullOrEmpty(message))
                {
                    SetCommitMessage(form, message);
                }
            }
            catch (NoStagedChangesException)
            {
                MessageBox.Show(form,
                    "No staged changes were found. Stage the files you want to commit, then click again.",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(form,
                    "Failed to generate a commit message:\n\n" + ex.Message,
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                button.Text = originalText;
                button.Enabled = true;
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

        // Sets the commit message using FormCommit's own ReplaceMessage(string), falling back to Message.Text.
        private static void SetCommitMessage(Form form, string message)
        {
            MethodInfo? replace = form.GetType().GetMethod(
                "ReplaceMessage",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null, types: new[] { typeof(string) }, modifiers: null);
            if (replace is not null)
            {
                replace.Invoke(form, new object[] { message });
                return;
            }

            if (GetMember(form, "Message") is Control messageControl)
            {
                messageControl.Text = message;
                messageControl.Focus();
            }
        }

        private static object? GetMember(Form form, string name)
        {
            FieldInfo? field = form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field?.GetValue(form);
        }

        private static Form? FindOpenForm(string typeName)
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f.GetType().Name == typeName && f.Visible && !f.IsDisposed)
                {
                    return f;
                }
            }

            return null;
        }

        private static IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control descendant in AllControls(child))
                {
                    yield return descendant;
                }
            }
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
