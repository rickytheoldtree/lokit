using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Lokit.Data;
using Lokit.DictConverter;
using Lokit.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lokit.Translate
{
    public class AIApiTranslator : IDisposable
    {
        private readonly string endpoint;
        private readonly string modelName;
        private readonly string prompt;
        private readonly HttpClient client = new();

        private AIApiTranslator(string apiKey, string endpoint, string modelName, string prompt)
        {
            this.endpoint = endpoint;
            this.modelName = modelName;
            this.prompt = prompt;
            try
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            catch (FormatException)
            {
                throw new ArgumentException("API Key格式不正确，请在设置页重新填写", nameof(apiKey));
            }
        }

        public static async Task<bool> Run(CancellationToken token)
        {
            return await Task.Run(async () =>
            {
                var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
                if (dictConverter == null)
                {
                    throw new InvalidOperationException("字典转换器类型错误");
                }

                using var translator = new AIApiTranslator(ConfigUtils.ApiKey.Value, ConfigUtils.ApiEndpoint.Value,
                    ConfigUtils.AIModel.Value, ConfigUtils.AIPrompt.Value);

                return await TranslationJob.RunAsync(dictConverter, translator.TranslateAsync, null, token);
            }, token);
        }

        private Task<Dictionary<string, string>?> TranslateAsync(Pair from, Pair to, Dictionary<string, string> source,
            IDictConverter.IDictConverter converter, CancellationToken token)
        {
            return TranslationJob.TranslateWithAiAsync(source, to, converter, Translate, token);
        }

        private async Task<string> Translate(string text, Pair to, CancellationToken token)
        {
            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "user", content = TranslationJob.FormatPrompt(prompt, to, text) }
                }
            };

            using var httpContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8,
                "application/json");

            using var response = await client.PostAsync(endpoint, httpContent, token);
            var responseContent = await response.Content.ReadAsStringAsync(token);
            if (token.IsCancellationRequested)
            {
                return string.Empty;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"API request failed: {response.StatusCode}, {responseContent}",
                    null, response.StatusCode);
            }

            try
            {
                var envelope = JToken.Parse(responseContent);
                var content = envelope["choices"]?[0]?["message"]?["content"]?.ToString()
                              ?? envelope["choices"]?[0]?["text"]?.ToString()
                              ?? envelope["output_text"]?.ToString();
                // Some compatible endpoints return the translated object itself instead of an
                // OpenAI-style envelope. Let the shared structured-response parser validate it.
                return content ?? responseContent;
            }
            catch (JsonException)
            {
                // A few proxy endpoints return model text directly despite a JSON content type.
                // It may still contain a valid fenced/embedded payload, so validate it upstream.
                return responseContent;
            }
        }

        public void Dispose()
        {
            client.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
