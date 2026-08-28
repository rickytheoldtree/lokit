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

        private const int PollIntervalMs = 100;

        /// <summary>登录需要人工操作，保留较长等待时间。</summary>
        private const int LoginWaitTryCount = 100000;

        /// <summary>页面跳转后等待聊天输入区的最长时间（60秒）。</summary>
        private const int PageReadyTryCount = 600;

        /// <summary>点击发送后等待页面接受请求的最长时间（30秒）。</summary>
        private const int SendWaitTryCount = 300;

        /// <summary>等待模型给出结束标记；可随时通过界面的“取消任务”终止。</summary>
        private const int ReplyWaitTryCount = 100000;

        // 2026-08 的 DeepSeek 页面使用 name=search；placeholder 作为后备，避免只依赖易变的哈希 class。
        private static readonly By ComposerBy =
            By.CssSelector("textarea[name='search'], textarea[placeholder*='DeepSeek']");

        // 当前发送控件是 role=button 的 div，并不是 button 标签。
        private static readonly By SendButtonBy =
            By.CssSelector("[role='button'].ds-button--primary.ds-button--circle");

        // 当前回复正文同时带有这两个 class；第二个选择器用于兼容旧页面。
        private static readonly By AssistantMessageBy =
            By.CssSelector(".ds-assistant-message-main-content");
        private static readonly By LegacyAssistantMessageBy = By.CssSelector(".ds-markdown");

        // “深度思考”现在是 aria-pressed 开关，而不是旧版 button/span 结构。
        private static readonly By DeepThinkingToggleBy = By.XPath(
            "//*[@aria-pressed and .//*[normalize-space(text())='深度思考']]");

        /// <summary>
        /// DeepSeek 的输入框由前端框架控制。必须调用 textarea 原型上的 setter 并触发 input 事件，
        /// 只修改 DOM 的 value 属性不会同步到页面状态，随后按回车时页面仍会把它当作空输入框。
        /// </summary>
        private const string SetTextAreaValueScript = """
            const textArea = arguments[0];
            const value = arguments[1];
            const valueSetter = Object.getOwnPropertyDescriptor(
                HTMLTextAreaElement.prototype, 'value').set;
            valueSetter.call(textArea, value);
            textArea.dispatchEvent(new Event('input', { bubbles: true }));
            textArea.dispatchEvent(new Event('change', { bubbles: true }));
            """;

        private readonly string prompt;
        private readonly bool deepThinking;

        private DeepSeekWebDriverTranslator(Config.WebDriverType type, string driverPath, string prompt,
            bool deepThinking)
            : base(type, driverPath)
        {
            this.prompt = prompt;
            this.deepThinking = deepThinking;
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
                    new DeepSeekWebDriverTranslator(driver.Type, driver.DriverPath, ConfigUtils.AIPrompt.Value,
                        deepThinking);
                if (!await translator.LoginTask(token))
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

        private async Task<bool> LoginTask(CancellationToken token)
        {
            if (!ToUrl(Url)) return false;
            LogUtils.Log("等待登录...");
            var textArea = await WaitForVisibleElement(ComposerBy, LoginWaitTryCount, token);
            if (textArea == null)
            {
                LogUtils.LogError("登录超时");
                return false;
            }

            return true;
        }

        private async Task<string> Translate(string text, Pair to, CancellationToken token)
        {
            if (!ToUrl(Url)) return "";

            var contentText = TranslationJob.FormatPrompt(prompt, to, text);
            var textArea = await WaitForVisibleElement(ComposerBy, PageReadyTryCount, token);
            if (textArea == null)
            {
                LogUtils.LogError("DeepSeek聊天输入区加载超时，请检查登录状态或页面是否更新");
                return "";
            }

            if (!await SetDeepThinkingMode(token)) return "";

            if (!await FillTextArea(textArea, contentText, token)) return "";

            LogUtils.Log("翻译内容已填入，正在发送...");
            if (!await SendPrompt(textArea, token)) return "";

            LogUtils.Log("等待翻译完成...");
            return await WaitForReply(token);
        }

        private async Task<bool> SetDeepThinkingMode(CancellationToken token)
        {
            var toggle = await WaitForVisibleElement(DeepThinkingToggleBy, 50, token);
            if (toggle == null)
            {
                if (!deepThinking) return true;

                LogUtils.LogError("未找到DeepSeek深度思考开关，无法按所选模式继续");
                return false;
            }

            try
            {
                var isPressed = string.Equals(toggle.GetAttribute("aria-pressed"), "true",
                    StringComparison.OrdinalIgnoreCase);
                if (isPressed == deepThinking) return true;

                toggle.Click();
                for (var count = 0; count < 30; count++)
                {
                    token.ThrowIfCancellationRequested();
                    if (string.Equals(toggle.GetAttribute("aria-pressed"), deepThinking ? "true" : "false",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    await Task.Delay(PollIntervalMs, token);
                }
            }
            catch (WebDriverException e)
            {
                LogUtils.LogError($"设置DeepSeek深度思考失败：{e.Message}");
                return false;
            }

            LogUtils.LogError("DeepSeek深度思考开关未切换到所选状态");
            return false;
        }

        private async Task<bool> FillTextArea(IWebElement textArea, string contentText, CancellationToken token)
        {
            try
            {
                textArea.Click();
                var js = (IJavaScriptExecutor)webDriver!;
                js.ExecuteScript(SetTextAreaValueScript, textArea, contentText);

                //不能只检查 DOM value；只有发送控件同步变为可用，才说明前端状态真正收到了内容。
                if (await WaitUntilInputAccepted(textArea, contentText, 20, token))
                {
                    return true;
                }

                LogUtils.LogError("DeepSeek输入框未接受脚本填充，改用键盘输入");
                textArea.Click();
                textArea.SendKeys(Keys.Control + "a");
                textArea.SendKeys(Keys.Backspace);
                textArea.SendKeys(contentText);
                if (await WaitUntilInputAccepted(textArea, contentText, 50, token))
                {
                    return true;
                }
            }
            catch (WebDriverException e)
            {
                LogUtils.LogError($"填写DeepSeek输入框失败：{e.Message}");
                return false;
            }

            LogUtils.LogError("填写DeepSeek输入框失败：输入内容未出现在页面中");
            return false;
        }

        private async Task<bool> WaitUntilInputAccepted(IWebElement textArea, string contentText, int tryCount,
            CancellationToken token)
        {
            for (var count = 0; count < tryCount; count++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (textArea.GetDomProperty("value") == contentText)
                    {
                        var sendButton = FindVisibleElement(SendButtonBy);
                        // 找不到新版发送控件时保留回车发送兼容；找到时则必须确认它已解除禁用。
                        if (sendButton == null || !IsDisabledButton(sendButton))
                        {
                            return true;
                        }
                    }
                }
                catch (StaleElementReferenceException)
                {
                    return false;
                }

                await Task.Delay(PollIntervalMs, token);
            }

            return false;
        }

        private async Task<bool> SendPrompt(IWebElement textArea, CancellationToken token)
        {
            try
            {
                var sendButton = FindVisibleElement(SendButtonBy);
                if (sendButton != null && !IsDisabledButton(sendButton))
                {
                    sendButton.Click();
                }
                else
                {
                    // 兼容没有语义 class 的旧页面。
                    textArea.SendKeys(Keys.Enter);
                }
            }
            catch (WebDriverException e)
            {
                LogUtils.LogError($"发送DeepSeek翻译请求失败：{e.Message}");
                return false;
            }

            for (var count = 0; count < SendWaitTryCount; count++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var currentTextArea = FindVisibleElement(ComposerBy);
                    var inputCleared = currentTextArea != null &&
                                       string.IsNullOrEmpty(currentTextArea.GetDomProperty("value"));
                    var enteredConversation = webDriver?.Url.Contains("/a/chat/s/",
                        StringComparison.OrdinalIgnoreCase) == true;
                    var hasAssistantMessage = FindLatestAssistantMessage() != null;
                    if (inputCleared && (enteredConversation || hasAssistantMessage))
                    {
                        return true;
                    }
                }
                catch (WebDriverException)
                {
                    // 页面从新对话跳转到会话地址时元素会短暂失效，继续重新查找。
                }

                await Task.Delay(PollIntervalMs, token);
            }

            LogUtils.LogError("DeepSeek未接受发送操作，请检查输入框或发送按钮是否再次更新");
            return false;
        }

        private async Task<string> WaitForReply(CancellationToken token)
        {
            for (var count = 0; count < ReplyWaitTryCount; count++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var block = FindLatestAssistantMessage();
                    var textBlock = block?.Text;
                    if (!string.IsNullOrEmpty(textBlock))
                    {
                        var endIndex = textBlock.IndexOf(EndMark, StringComparison.Ordinal);
                        if (endIndex >= 0)
                        {
                            return textBlock[..endIndex].Trim();
                        }
                    }

                    if (webDriver?.Url.Contains("sign_in", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        LogUtils.LogError("DeepSeek登录状态已失效");
                        return "";
                    }
                }
                catch (WebDriverException)
                {
                    // 流式渲染会替换回复节点，下一轮重新获取最新元素。
                }

                await Task.Delay(PollIntervalMs, token);
            }

            LogUtils.LogError("等待DeepSeek翻译结果超时");
            return "";
        }

        private IWebElement? FindLatestAssistantMessage()
        {
            var messages = FindVisibleElements(AssistantMessageBy);
            if (messages.Count == 0)
            {
                messages = FindVisibleElements(LegacyAssistantMessageBy);
            }

            return messages.Count == 0 ? null : messages[^1];
        }

        private async Task<IWebElement?> WaitForVisibleElement(By by, int tryCount, CancellationToken token)
        {
            for (var count = 0; count < tryCount; count++)
            {
                token.ThrowIfCancellationRequested();
                var element = FindVisibleElement(by);
                if (element != null) return element;
                await Task.Delay(PollIntervalMs, token);
            }

            return null;
        }

        private IWebElement? FindVisibleElement(By by)
        {
            var elements = FindVisibleElements(by);
            return elements.Count == 0 ? null : elements[0];
        }

        private System.Collections.Generic.List<IWebElement> FindVisibleElements(By by)
        {
            var result = new System.Collections.Generic.List<IWebElement>();
            if (webDriver == null) return result;

            try
            {
                foreach (var element in webDriver.FindElements(by))
                {
                    try
                    {
                        if (element.Displayed) result.Add(element);
                    }
                    catch (StaleElementReferenceException)
                    {
                        // 页面正在重新渲染，忽略本轮失效元素。
                    }
                }
            }
            catch (WebDriverException)
            {
                // 由调用方的轮询决定继续等待或超时。
            }

            return result;
        }

        private static bool IsDisabledButton(IWebElement button)
        {
            if (string.Equals(button.GetAttribute("aria-disabled"), "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var className = button.GetAttribute("class") ?? "";
            return className.Contains("ds-button--disabled", StringComparison.Ordinal);
        }
    }
}
