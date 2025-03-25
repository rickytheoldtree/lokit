using System;
using System.Threading;
using System.Threading.Tasks;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Utils;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;

namespace LocalizationTranslationWPF.Translate;

public abstract class WebDriverTranslator : IDisposable
{
    protected readonly IWebDriver? webDriver;
    private readonly DriverService? service;

    protected WebDriverTranslator(Config.WebDriverType type)
    {
        try
        {
            switch (type)
            {
                default:
                case Config.WebDriverType.Edge:
                    service = EdgeDriverService.CreateDefaultService(PathUtils.DriverPath);
                    service.HideCommandPromptWindow = true;
                    webDriver = new EdgeDriver(service as EdgeDriverService);
                    break;
                case Config.WebDriverType.Chrome:
                    service = ChromeDriverService.CreateDefaultService(PathUtils.DriverPath);
                    service.HideCommandPromptWindow = true;
                    webDriver = new ChromeDriver(service as ChromeDriverService);
                    break;
                case Config.WebDriverType.FireFox:
                    service = FirefoxDriverService.CreateDefaultService(PathUtils.DriverPath);
                    service.HideCommandPromptWindow = true;
                    webDriver = new FirefoxDriver(service as FirefoxDriverService);
                    break;
            }
        }
        catch
        {
            webDriver?.Quit();
            service?.Dispose();
            throw;
        }
    }

    protected async Task<IWebElement?> FindElementBy(By by, int tryCount, CancellationToken token)
    {
        var count = 0;
        IWebElement? element = null;

        while (element == null && count < tryCount)
        {
            try
            {
                element = webDriver?.FindElement(by);
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

            count++;
            if (element != null)
            {
                return element;
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

    protected static Task RandomDelay(Random random)
    {
        var randomDelay = random.Next(3000, 8000);
        LogUtils.Log($"随机延迟: {randomDelay}ms");
        return Task.Delay(randomDelay);
    }
    public void Dispose()
    {
        webDriver?.Quit();
        service?.Dispose();
    }
}