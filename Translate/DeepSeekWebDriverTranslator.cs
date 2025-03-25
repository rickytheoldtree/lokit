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
using OpenQA.Selenium;

namespace LocalizationTranslationWPF.Translate
{
    public class DeepSeekWebDriverTranslator : WebDriverTranslator
    {
        private const string Prompt = "请将 {0} 翻译成 {1}，只回答我json格式，不要回答额外信息，也不用加json的markdown";
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
                using var translator = new DeepSeekWebDriverTranslator(ConfigUtils.WebDriver.Value, ConfigUtils.Config.aiPrompt);
                
                if(!await translator.LoginTask(deepThinking, token))
                    return;
                
                if (targetLang.Name == "All")
                {
                    foreach (var pair in PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value))
                    {
                        targetLang = pair;
                        if (!await translator.AITranslateTask(sourceLang, targetLang, dictConverter, token))
                        {
                            throw new Exception($"翻译{targetLang.Name}失败");
                        }
                    }
                }
                else
                {
                    await translator.AITranslateTask(sourceLang, targetLang, dictConverter, token);
                }
            }, token);
        }

        private async Task<bool> AITranslateTask(Pair from, Pair to, IDictConverter.IDictConverter converter,
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

        private async Task<bool> LoginTask(bool deepThink, CancellationToken token)
        {
            if(!ToUrl(url)) return false;
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
            
            var contentText = string.Format(Prompt, text, to.Name);
            var textArea = await FindElementBy(By.XPath("//textarea[@id='chat-input']"), 100000, token);
            if(textArea == null) return "";
            textArea.SendKeys(contentText);
            
            var btnSend = await FindElementBy(By.XPath("//div[@role='button' and @aria-disabled='false']"), 100000, token);
            if (btnSend == null) return "";
            btnSend.Click();
            
            var block = await FindElementBy(By.XPath("//div[@class='ds-markdown ds-markdown--block']"), 100000, token);
            if (block == null) return "";
            while (block.Text[^1] != '}')
            {
                await Task.Delay(100, token);
            }
            return block.Text;
        }
    }
}