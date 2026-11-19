using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GitExtensions.AICommitMessage
{
    /// <summary>
    /// Minimal client for the OpenAI-compatible Chat Completions endpoint
    /// (<c>{baseUrl}/chat/completions</c>). The same shape is accepted by OpenAI, Azure OpenAI,
    /// OpenRouter, Groq, and local servers such as Ollama, so one code path covers cloud and local.
    /// </summary>
    internal sealed class OpenAiClient
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly double? _temperature;

        public OpenAiClient(string baseUrl, string apiKey, string model, string? temperature = null)
        {
            _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            _apiKey = (apiKey ?? string.Empty).Trim();
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim();

            if (!string.IsNullOrWhiteSpace(temperature))
            {
                if (!double.TryParse(temperature, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    || !double.IsFinite(value) || value < 0 || value > 2)
                {
                    throw new ArgumentException(
                        "Temperature must be blank (model default) or a number from 0 to 2 using a decimal point, such as 0.2.",
                        nameof(temperature));
                }

                _temperature = value;
            }
        }

        public async Task<string> CompleteAsync(string systemPrompt, string diff, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
            {
                throw new InvalidOperationException(
                    "API base URL is not configured. Set it in Settings → Plugins → AI Commit Message.");
            }

            var requestBody = new Dictionary<string, object>
            {
                ["model"] = _model,
                ["messages"] = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = "Here is the staged git diff. Write the commit message for it:\n\n" + diff }
                }
            };

            if (_temperature.HasValue)
            {
                requestBody["temperature"] = _temperature.Value;
            }

            string json = JsonSerializer.Serialize(requestBody);

            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(90) };
            using HttpRequestMessage request = new(HttpMethod.Post, _baseUrl + "/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrEmpty(_apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"The API returned {(int)response.StatusCode} ({response.ReasonPhrase}).\n\n{Truncate(responseText, 1000)}");
            }

            return ExtractContent(responseText);
        }

        private static string ExtractContent(string responseText)
        {
            using JsonDocument doc = JsonDocument.Parse(responseText);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("choices", out JsonElement choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                JsonElement first = choices[0];
                if (first.TryGetProperty("message", out JsonElement message)
                    && message.TryGetProperty("content", out JsonElement content))
                {
                    return content.GetString()?.Trim() ?? string.Empty;
                }
            }

            throw new InvalidOperationException(
                "Unexpected API response shape (no choices[0].message.content):\n\n" + Truncate(responseText, 1000));
        }

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
