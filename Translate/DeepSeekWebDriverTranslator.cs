using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Utils;
using OpenQA.Selenium;

namespace LocalizationTranslationWPF.Translate
{
    public class DeepSeekWebDriverTranslator : WebDriverTranslator
    {
        private readonly string url = "https://chat.deepseek.com/";
        private readonly string prompt;

        private DeepSeekWebDriverTranslator(Config.WebDriverType type, string prompt) : base(type)
        {
            this.prompt = prompt;
        }

        public static async Task Run(CancellationToken token, bool deepThinking)
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
                using var translator =
                    new DeepSeekWebDriverTranslator(ConfigUtils.WebDriver.Value, ConfigUtils.Config.aiPrompt);
                var random = new Random();
                if (!await translator.LoginTask(deepThinking, token))
                    return;

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

                            await RandomDelay(random);
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
                            await RandomDelay(random);
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
            
            if (from.isoCode == to.isoCode)
            {
                IOUtils.OutputJson($"{PathUtils.OutputPath}\\{to.Name}.json", originJson, converter);
                LogUtils.Log($"跳过翻译{to.Name}，因为与源语言相同");
                return true;
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

        private async Task<bool> LoginTask(bool deepThink, CancellationToken token)
        {
            if (!ToUrl(url)) return false;
            LogUtils.Log("等待登录...");

            var textArea = await FindElementBy(By.XPath("//textarea[@id='chat-input']"), 100000, token);
            if (textArea == null)
            {
                LogUtils.LogError("登录超时");
                return false;
            }

            if (!deepThink) return true;

            await Task.Delay(500, token);
            var btnDeepThink = webDriver?.FindElement(By.XPath("//span[text()='深度思考 (R1)']"));
            btnDeepThink?.Click();
            return true;
        }

        private async Task<string> Translate(string text, Pair to, CancellationToken token)
        {
            if (!ToUrl(url)) return "";
            var contentText = $"{string.Format(prompt, to.Name, text)}";
            var textArea = await FindElementBy(By.XPath("//textarea[@id='chat-input']"), 100000, token);
            if (textArea == null) return "";
            var thread = new Thread(() => Clipboard.SetText(contentText));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            await Task.Delay(500, token);
            textArea.Click();
            await Task.Delay(500, token);
            textArea.SendKeys(Keys.Control + "v");

            var btnSend = await FindElementBy(By.XPath("//div[@role='button' and @aria-disabled='false']"), 100000,
                token);
            if (btnSend == null) return "";
            btnSend.Click();

            var block = await FindElementBy(By.XPath("//div[@class='ds-markdown ds-markdown--block']"), 100000, token);
            if (block == null) return "";
            while (!block.Text.Contains("[end]"))
            {
                await Task.Delay(100, token);
            }
            var textBlock = block.Text;
            var endIndex = textBlock.IndexOf("[end]", StringComparison.Ordinal);
            if (endIndex != -1)
            {
                textBlock = textBlock[..endIndex];
            }
            textBlock = textBlock.Replace("[end]", "");
            return textBlock;
        }
    }
}