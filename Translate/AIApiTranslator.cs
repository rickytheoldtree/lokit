using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Utils;
using Newtonsoft.Json.Linq;

namespace LocalizationTranslationWPF.Translate
{
    public class AIApiTranslator : IDisposable
    {
        private readonly string apiKey;
        private readonly string endpoint;
        private readonly string modelName;
        private readonly string prompt;
        private readonly HttpClient client = new();

        private AIApiTranslator(string apiKey, string endpoint, string modelName, string prompt)
        {
            this.apiKey = apiKey;
            this.endpoint = endpoint;
            this.modelName = modelName;
            this.prompt = prompt;
            try
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.apiKey);
            }
            catch (FormatException)
            {
                throw new Exception("API Key格式不正确，请在设置页重新填写");
            }
        }

        public static async Task Run(CancellationToken token)
        {
            await Task.Run(async () =>
            {
                var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
                if (dictConverter == null)
                {
                    throw new Exception("字典转换器类型错误");
                }

                using var translator = new AIApiTranslator(ConfigUtils.ApiKey.Value, ConfigUtils.ApiEndpoint.Value,
                    ConfigUtils.AIModel.Value, ConfigUtils.AIPrompt.Value);

                await TranslationJob.RunAsync(dictConverter, translator.TranslateAsync, null, token);
            }, token);
        }

        private Task<bool> TranslateAsync(Pair from, Pair to, IDictConverter.IDictConverter converter,
            CancellationToken token)
        {
            return TranslationJob.TranslateWithAiAsync(from, to, converter, Translate, token);
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

            var httpContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync(endpoint, httpContent, token);
            var responseContent = await response.Content.ReadAsStringAsync(token);
            if (token.IsCancellationRequested)
            {
                return string.Empty;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"API request failed: {response.StatusCode}, {responseContent}");
            }

            var content = JObject.Parse(responseContent)["choices"]?[0]?["message"]?["content"]?.ToString();
            if (content == null)
            {
                LogUtils.LogError($"接口返回内容无法解析：{responseContent}");
                return string.Empty;
            }

            return content;
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
