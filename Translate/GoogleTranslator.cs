using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Utils;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LocalizationTranslationWPF.Translate
{
    public class GoogleTranslator
    {
        private static GoogleTranslator? instance;
        private readonly Config.WebDriverType webDriverType;
        private readonly IWebDriver webDriver;
        private string url = "";

        public static GoogleTranslator GetInstance(Config.WebDriverType type)
        {
            if (instance == null)
            {
                instance = new GoogleTranslator(type);
            }
            else if (instance.webDriverType != type)
            {
                instance = new GoogleTranslator(type);
            }

            return instance;
        }

        private GoogleTranslator(Config.WebDriverType type)
        {
            webDriverType = Config.WebDriverType.Edge;
            try
            {
                switch (type)
                {
                    default:
                    case Config.WebDriverType.Edge:
                        var service = EdgeDriverService.CreateDefaultService(PathUtils.DriverPath);
                        service.HideCommandPromptWindow = true;
                        webDriver = new EdgeDriver(service);
                        break;
                    case Config.WebDriverType.Chrome:
                        var chromeService = ChromeDriverService.CreateDefaultService(PathUtils.DriverPath);
                        chromeService.HideCommandPromptWindow = true;
                        webDriver = new ChromeDriver(chromeService);
                        break;
                    case Config.WebDriverType.FireFox:
                        var fireFoxService = FirefoxDriverService.CreateDefaultService(PathUtils.DriverPath);
                        fireFoxService.HideCommandPromptWindow = true;
                        webDriver = new FirefoxDriver(fireFoxService);
                        break;
                }
            }
            catch
            {
                webDriver?.Quit();
                throw;
            }
        }

        public async Task<bool> Translate(string txt, string from, string to, Action<List<string>> callback,
            CancellationToken token)
        {
            url = $"https://translate.google.com/?hl=en&sl={from}" +
                  $"&tl={to}" +
                  $"&text={txt}" +
                  "&op=translate";
            try
            {
                webDriver.Navigate().GoToUrl(url);
            }
            catch (Exception e)
            {
                LogUtils.LogError(e.Message);
                return false;
            }

            //每隔1秒尝试1次获取翻译结果，最多尝试10次
            var count = 0;
            bool success = false;
            while (count < 10)
            {
                try
                {
                    // find button aria-label="Copy translation"
                    var button = webDriver.FindElement(By.CssSelector("button[aria-label='Copy translation']"));
                    if (button != null)
                    {
                        button.Click();
                        success = true;
                        break;
                    }
                }
                catch (Exception e)
                {
                    if (e is NoSuchWindowException)
                    {
                        LogUtils.LogError(e.Message);
                        return false;
                    }
                }

                count++;
                await Task.Delay(1000, token);
                if (token.IsCancellationRequested)
                {
                    return false;
                }
            }

            if (!success)
            {
                //弹窗
                LogUtils.LogError("尝试10次后还没翻译完，检查一下网络，或者换一个vpn吧");
                return false;
            }

            await Task.Delay(1000, token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            //访问Windows粘贴板
            var text = ThreadUtils.GetClipboard();
            if (!TaskUtils.Txt2StrList(text, out var values))
            {
                return false;
            }

            callback.Invoke(values);
            return true;
        }

        public static string GetUrlSafeString(string str)
        {
            str = str.Replace("%", "%25");
            str = str.Replace(" ", "%20");
            str = str.Replace("=", "%3D");
            str = str.Replace("#", "%23");
            str = str.Replace("&", "%26");
            str = str.Replace("+", "%2B");
            return str;
        }
    }
}