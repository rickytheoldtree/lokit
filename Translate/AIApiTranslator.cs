using System;
using System.Collections.Generic;
using System.IO;
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
        private const string DefaultPrompt =
            "接下来你将翻译我提供的json格式文件，你需要翻译成纯文本形式的json，一定一定不要markdown代码块，否则对我来说没有意义!" +
            "如果翻译内容里出现了占位符，请务必保留，请确保翻译后的文本中也有这些标记，且数量不变。" +
            "那么现在，请将 {0} 翻译成 {1}";

        private readonly string apiKey;
        private readonly string endpoint;
        private readonly string modelName;
        private readonly string prompt;

        private AIApiTranslator(string apiKey, string endpoint, string modelName, string prompt)
        {
            this.apiKey = apiKey;
            this.endpoint = endpoint;
            this.modelName = modelName;
            this.prompt = prompt;
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

                var sourceLang = ConfigUtils.SourceLang;
                var targetLang = ConfigUtils.TargetLang;
                using var translator = new AIApiTranslator(ConfigUtils.Config.aiAPIKey, ConfigUtils.Config.aiEndpoint,
                    ConfigUtils.Config.aiModel, ConfigUtils.Config.aiPrompt);

                switch (ConfigUtils.TargetLangAmount.Value)
                {
                    case Config.TargetLangAmount.All:
                    {
                        foreach (var pair in PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value))
                        {
                            token.ThrowIfCancellationRequested();
                            targetLang = pair;
                            if (!await translator.TranslateAsync(sourceLang, targetLang, dictConverter,
                                    token))
                            {
                                LogUtils.LogError($"翻译{targetLang.Name}失败");
                            }
                        }
                        break;
                    }
                    case Config.TargetLangAmount.Remain:
                    {
                        var pairs = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);
                        var index = pairs.FindIndex(p => p.isoCode == targetLang.isoCode);
                        if (index == -1)
                        {
                            LogUtils.LogError("目标语言不在剩余语言列表中");
                            return;
                        }

                        for (var i = index; i < pairs.Count; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            targetLang = pairs[i];
                            if (!await translator.TranslateAsync(sourceLang, targetLang, dictConverter,
                                    token))
                            {
                                LogUtils.LogError($"翻译{targetLang.Name}失败");
                            }
                        }

                        break;
                    }
                    case Config.TargetLangAmount.Single:
                    default:
                        await translator.TranslateAsync(sourceLang, targetLang, dictConverter, token);
                        break;
                }
            }, token);
        }

        private async Task<bool> TranslateAsync(Pair from, Pair to, IDictConverter.IDictConverter converter,
            CancellationToken token)
        {
            if (from.isoCode == to.isoCode)
            {
                LogUtils.Log($"跳过翻译{to.Name}，因为与源语言相同");
                return true;
            }

            var originJsonPath = $"{PathUtils.InputPath}\\{from.Name}.json";
            if (!File.Exists(originJsonPath))
            {
                LogUtils.LogError($"{PathUtils.InputPath}\\{from.Name}.json不存在");
                return false;
            }

            if (!IOUtils.TryInputJson(originJsonPath, converter, out var originJson) || originJson == null)
            {
                LogUtils.LogError($"{PathUtils.InputPath}\\{from.Name}.json读取出错，检查项目标准");
                return false;
            }

            string result = await Translate(converter.Serialize(originJson), to, token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            Dictionary<string, string>? dic;
            try
            {
                if (!converter.TryDeserialize(result, out dic) || dic == null)
                {
                    LogUtils.LogError($"{from.Name}翻译失败，ai翻译结果格式错误");
                    return false;
                }
            }
            catch
            {
                LogUtils.LogError($"{from.Name}翻译失败，ai翻译结果格式错误");
                return false;
            }

            if (!Directory.Exists(PathUtils.OutputPath))
            {
                Directory.CreateDirectory(PathUtils.OutputPath);
            }

            IOUtils.OutputJson($"{PathUtils.OutputPath}\\{to.Name}.json", dic, converter);
            LogUtils.Log($"{to.Name}翻译完成，文件路径：{PathUtils.OutputPath}\\{to.Name}.json");
            return true;
        }

        private async Task<string> Translate(string text, Pair to, CancellationToken token)
        {
            // Format the prompt with the input text and target language (to.Name)
            var contentText = string.Format(DefaultPrompt, text, to.Name);

            // Prepare the request payload
            var requestBody = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "system", content = prompt },
                    new { role = "user", content = contentText }
                }
            };

            var jsonRequestBody = JsonConvert.SerializeObject(requestBody);

            using var client = new HttpClient();
            // Set the authorization header with your API key
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var httpContent = new StringContent(jsonRequestBody, Encoding.UTF8, "application/json");

            // Send POST request to the ChatGPT API endpoint
            var response = await client.PostAsync(endpoint, httpContent, token);
            if (token.IsCancellationRequested)
            {
                return string.Empty;
            }

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(token);
                if (token.IsCancellationRequested)
                {
                    return string.Empty;
                }

                var jsonResponse = JObject.Parse(responseContent);
                return jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString();
            }

            var errorContent = await response.Content.ReadAsStringAsync(token);
            if (token.IsCancellationRequested)
            {
                return string.Empty;
            }

            throw new Exception($"API request failed: {response.StatusCode}, {errorContent}");
        }

        public void Dispose()
        {
        }
    }
}