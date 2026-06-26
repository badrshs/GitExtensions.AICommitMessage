using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace GitExtensions.AICommitMessage
{
    internal static class GitHelper
    {
        /// <summary>
        /// Returns the staged diff (<c>git diff --cached</c>) for <paramref name="workingDir"/>,
        /// or an empty string if nothing is staged. Only staged content is read, so files excluded
        /// by .gitignore are never included.
        /// </summary>
        public static string GetStagedDiff(string workingDir)
        {
            ProcessStartInfo psi = new()
            {
                FileName = "git",
                Arguments = "--no-pager diff --cached --no-color",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using Process? process = Process.Start(psi);
            if (process is null)
            {
                throw new InvalidOperationException("Could not start 'git'. Ensure Git is installed and on PATH.");
            }

            // Read stderr concurrently so a large diff on stdout cannot deadlock the pipe.
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30000);
            string error = errorTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0 && string.IsNullOrEmpty(output))
            {
                throw new InvalidOperationException("'git diff --cached' failed: " + error);
            }

            return output;
        }
    }
}
