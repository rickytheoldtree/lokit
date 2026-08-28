using System;
using System.Threading;
using System.Threading.Tasks;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Utils;
using OpenQA.Selenium;

namespace LocalizationTranslationWPF.Translate
{
    public class DeepSeekWebDriverTranslator : WebDriverTranslator
    {
        private const string Url = "https://chat.deepseek.com/";

        /// <summary>
        /// 结束标记，模型输出到这里说明本次翻译已完整
        /// </summary>
        private const string EndMark = "[end]";

        /// <summary>
        /// 等待页面元素的重试次数（每次间隔100ms），登录需要人工操作所以给得很长
        /// </summary>
        private const int WaitElementTryCount = 100000;

        private readonly string prompt;

        private DeepSeekWebDriverTranslator(Config.WebDriverType type, string driverPath, string prompt)
            : base(type, driverPath)
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

                var driver = await WebDriverProvider.PrepareAsync(ConfigUtils.WebDriver.Value, token);
                using var translator =
                    new DeepSeekWebDriverTranslator(driver.Type, driver.DriverPath, ConfigUtils.AIPrompt.Value);
                if (!await translator.LoginTask(deepThinking, token))
                {
                    return;
                }
                LogUtils.Log("登录成功");

                var random = new Random();
                await TranslationJob.RunAsync(dictConverter, translator.TranslateAsync,
                    () => RandomDelay(random, token), token);
            }, token);
        }

        private Task<bool> TranslateAsync(Pair from, Pair to, IDictConverter.IDictConverter converter,
            CancellationToken token)
        {
            return TranslationJob.TranslateWithAiAsync(from, to, converter, Translate, token);
        }

        private async Task<bool> LoginTask(bool deepThink, CancellationToken token)
        {
            if (!ToUrl(Url)) return false;
            LogUtils.Log("等待登录...");
            var textArea = await FindElementBy(By.TagName("textarea"), WaitElementTryCount, token);
            if (textArea == null)
            {
                LogUtils.LogError("登录超时");
                return false;
            }

            if (!deepThink) return true;

            await Task.Delay(500, token);
            var btnDeepThink = await FindElementBy(By.XPath("//button[.//span[contains(text(),'深度思考')]]"), 50, token);
            if (btnDeepThink == null)
            {
                LogUtils.LogError("未找到深度思考按钮，将以普通模式继续");
                return true;
            }
            btnDeepThink.Click();
            return true;
        }

        private async Task<string> Translate(string text, Pair to, CancellationToken token)
        {
            if (!ToUrl(Url)) return "";

            var contentText = TranslationJob.FormatPrompt(prompt, to, text);
            var textArea = await FindElementBy(By.TagName("textarea"), WaitElementTryCount, token);
            if (textArea == null) return "";

            textArea.Click();
            await Task.Delay(500, token);
            //输入框是受控组件，直接赋值后再补一个按键触发它的 change 事件
            var js = (IJavaScriptExecutor)webDriver!;
            js.ExecuteScript("arguments[0].value = arguments[1];", textArea, contentText);
            textArea.SendKeys(" ");
            await Task.Delay(500, token);
            textArea.SendKeys(Keys.Enter);

            var block = await FindElementBy(By.XPath("//p[@class='ds-markdown-paragraph']"), WaitElementTryCount, token);
            LogUtils.Log("等待翻译完成...");
            if (block == null) return "";

            while (!block.Text.Contains(EndMark))
            {
                await Task.Delay(100, token);
            }

            var textBlock = block.Text;
            var endIndex = textBlock.IndexOf(EndMark, StringComparison.Ordinal);
            return endIndex == -1 ? textBlock : textBlock[..endIndex];
        }
    }
}
