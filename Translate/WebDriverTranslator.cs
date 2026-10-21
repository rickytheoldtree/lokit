using System.IO;
using Lokit.Data;
using Lokit.Utils;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;

namespace Lokit.Translate;

public abstract class WebDriverTranslator : IDisposable
{
    private readonly IWebDriver? webDriver;
    private readonly DriverService? service;
    private bool disposed;

    protected IWebDriver? WebDriver => webDriver;

    /// <param name="type"></param>
    /// <param name="driverPath">驱动的完整路径，由 <see cref="WebDriverProvider"/> 解析得到</param>
    protected WebDriverTranslator(Config.WebDriverType type, string driverPath)
    {
        var folder = Path.GetDirectoryName(driverPath);
        var fileName = Path.GetFileName(driverPath);
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException($"驱动路径无效：{driverPath}", nameof(driverPath));
        }

        try
        {
            switch (type)
            {
                case Config.WebDriverType.Chrome:
                    {
                        var chrome = ChromeDriverService.CreateDefaultService(folder, fileName);
                        chrome.HideCommandPromptWindow = true;
                        service = chrome;
                        webDriver = new ChromeDriver(chrome);
                        break;
                    }
                case Config.WebDriverType.FireFox:
                    {
                        var firefox = FirefoxDriverService.CreateDefaultService(folder, fileName);
                        firefox.HideCommandPromptWindow = true;
                        service = firefox;
                        webDriver = new FirefoxDriver(firefox);
                        break;
                    }
                case Config.WebDriverType.Edge:
                default:
                    {
                        var edge = EdgeDriverService.CreateDefaultService(folder, fileName);
                        edge.HideCommandPromptWindow = true;
                        service = edge;
                        webDriver = new EdgeDriver(edge);
                        break;
                    }
            }
        }
        catch
        {
            webDriver?.Quit();
            service?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 轮询查找元素，每次间隔100ms，找不到返回null
    /// </summary>
    protected async Task<IWebElement?> FindElementBy(By by, int tryCount, CancellationToken token)
    {
        for (var count = 0; count < tryCount; count++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var element = webDriver?.FindElement(by);
                if (element != null)
                {
                    return element;
                }
            }
            catch (NoSuchWindowException e)
            {
                LogUtils.LogError(e.Message);
                return null;
            }
            catch (NoSuchElementException)
            {
                // ignore
            }

            await Task.Delay(100, token);
        }

        return null;
    }

    protected bool ToUrl(string url)
    {
        try
        {
            webDriver?.Navigate().GoToUrl(url);
            return true;
        }
        catch (Exception e)
        {
            LogUtils.LogError(e.Message);
            return false;
        }
    }

    /// <summary>
    /// 批量翻译时的随机间隔，降低被判定为机器人的概率
    /// </summary>
    protected static Task RandomDelay(Random random, CancellationToken token)
    {
        var randomDelay = random.Next(1000, 4000);
        LogUtils.Log($"随机延迟: {randomDelay}ms");
        return Task.Delay(randomDelay, token);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        webDriver?.Quit();
        service?.Dispose();
        GC.SuppressFinalize(this);
    }
}
