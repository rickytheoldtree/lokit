using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LocalizationTranslationWPF.Utils;
using Microsoft.Win32;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using static LocalizationTranslationWPF.Data.Config;

namespace LocalizationTranslationWPF.Translate
{
    /// <summary>
    /// 负责让使用者完全不用关心 WebDriver：
    /// 自动挑选本机已安装的浏览器，再交给 Selenium Manager 下载版本匹配的驱动。
    /// 驱动缓存在 <see cref="PathUtils.DriverPath"/>，联网失败时回退到该目录下已有的驱动。
    /// </summary>
    public static class WebDriverProvider
    {
        /// <summary>
        /// 单次网络请求超时（秒）。Selenium Manager 默认 300 秒，CDN 抽风时会让人干等 5 分钟。
        /// </summary>
        private const int NetworkTimeoutSeconds = 60;

        /// <summary>
        /// 解析结果：最终使用的浏览器，以及对应驱动的完整路径
        /// </summary>
        public readonly struct Resolved
        {
            public Resolved(WebDriverType type, string driverPath)
            {
                Type = type;
                DriverPath = driverPath;
            }

            public WebDriverType Type { get; }
            public string DriverPath { get; }
        }

        private sealed class BrowserInfo
        {
            public BrowserInfo(WebDriverType type, string display, string browserExe, string driverExe,
                string[] fallbackPaths)
            {
                Type = type;
                Display = display;
                BrowserExe = browserExe;
                DriverExe = driverExe;
                FallbackPaths = fallbackPaths;
            }

            public WebDriverType Type { get; }
            public string Display { get; }

            /// <summary>浏览器可执行文件名，用于查注册表 App Paths</summary>
            public string BrowserExe { get; }

            /// <summary>驱动可执行文件名，用于在缓存目录里兜底查找</summary>
            public string DriverExe { get; }

            /// <summary>注册表查不到时的常见安装位置</summary>
            public string[] FallbackPaths { get; }
        }

        //顺序即 Auto 模式的优先级：Edge 在 Windows 上必定存在，且驱动源最稳定
        private static readonly BrowserInfo[] KnownBrowsers =
        {
            new(WebDriverType.Edge, "Edge", "msedge.exe", "msedgedriver.exe", new[]
            {
                Combine("ProgramFiles(x86)", "Microsoft", "Edge", "Application", "msedge.exe"),
                Combine("ProgramFiles", "Microsoft", "Edge", "Application", "msedge.exe"),
            }),
            new(WebDriverType.Chrome, "Chrome", "chrome.exe", "chromedriver.exe", new[]
            {
                Combine("ProgramFiles", "Google", "Chrome", "Application", "chrome.exe"),
                Combine("ProgramFiles(x86)", "Google", "Chrome", "Application", "chrome.exe"),
                Combine("LOCALAPPDATA", "Google", "Chrome", "Application", "chrome.exe"),
            }),
            new(WebDriverType.FireFox, "Firefox", "firefox.exe", "geckodriver.exe", new[]
            {
                Combine("ProgramFiles", "Mozilla Firefox", "firefox.exe"),
                Combine("ProgramFiles(x86)", "Mozilla Firefox", "firefox.exe"),
            }),
        };

        private static string Combine(string environmentVariable, params string[] parts)
        {
            var root = Environment.GetEnvironmentVariable(environmentVariable);
            return string.IsNullOrEmpty(root) ? "" : Path.Combine(new[] { root }.Concat(parts).ToArray());
        }

        /// <summary>
        /// 选好浏览器并拿到匹配的驱动。首次调用可能需要联网下载，之后走缓存。
        /// </summary>
        public static async Task<Resolved> PrepareAsync(WebDriverType requested, CancellationToken token)
        {
            var browser = Pick(requested);
            ApplyManagerEnvironment();
            ClearStaleLocks();

            LogUtils.Log($"正在准备{browser.Display}驱动（首次使用需联网下载，单次请求最长{NetworkTimeoutSeconds}秒）...");
            try
            {
                var path = await new DriverFinder(CreateOptions(browser.Type)).GetDriverPathAsync(token);
                if (!string.IsNullOrEmpty(path))
                {
                    LogUtils.Log($"{browser.Display}驱动就绪：{path}");
                    return new Resolved(browser.Type, path);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                LogUtils.LogError($"自动获取{browser.Display}驱动失败：{e.Message}");
            }

            //联网失败时，回退到本地已经下过或手动放进去的驱动
            var local = FindLocalDriver(browser.DriverExe);
            if (local != null)
            {
                LogUtils.Log($"改用本地已有驱动：{local}");
                return new Resolved(browser.Type, local);
            }

            throw new Exception($"无法获取{browser.Display}驱动。请检查网络连接，或在设置页填写驱动代理后重试；" +
                                $"也可以手动把 {browser.DriverExe} 放进 {PathUtils.DriverPath}");
        }

        private static BrowserInfo Pick(WebDriverType requested)
        {
            if (requested != WebDriverType.Auto)
            {
                var chosen = KnownBrowsers.FirstOrDefault(b => b.Type == requested);
                if (chosen != null)
                {
                    return chosen;
                }
            }

            foreach (var browser in KnownBrowsers)
            {
                if (FindBrowserPath(browser) == null) continue;
                LogUtils.Log($"自动选择浏览器：{browser.Display}");
                return browser;
            }

            throw new Exception("未检测到 Edge / Chrome / Firefox，请先安装其中任意一个浏览器");
        }

        /// <summary>
        /// 用环境变量配置 Selenium Manager：驱动存到本程序目录下，并禁止它顺手下载整个浏览器。
        /// 这些开关在 .NET 侧的 BrowserDiscoveryOptions 里没有暴露，只能走环境变量。
        /// </summary>
        private static void ApplyManagerEnvironment()
        {
            Directory.CreateDirectory(PathUtils.DriverPath);
            Environment.SetEnvironmentVariable("SE_CACHE_PATH", Path.GetFullPath(PathUtils.DriverPath));
            //没装对应浏览器时宁可报错，也不要静默下载几百MB的浏览器本体
            Environment.SetEnvironmentVariable("SE_AVOID_BROWSER_DOWNLOAD", "true");
            //版本信息缓存一天，避免每小时都联网查一次
            Environment.SetEnvironmentVariable("SE_TTL", "86400");
            //限制单次网络请求耗时，失败要快，别让使用者盯着一行日志干等
            Environment.SetEnvironmentVariable("SE_TIMEOUT", NetworkTimeoutSeconds.ToString());

            var proxy = ConfigUtils.DriverProxy.Value;
            Environment.SetEnvironmentVariable("SE_PROXY", string.IsNullOrWhiteSpace(proxy) ? null : proxy.Trim());
        }

        private static DriverOptions CreateOptions(WebDriverType type)
        {
            return type switch
            {
                WebDriverType.Chrome => new ChromeOptions(),
                WebDriverType.FireFox => new FirefoxOptions(),
                _ => new EdgeOptions(),
            };
        }

        private static string? FindBrowserPath(BrowserInfo browser)
        {
            var fromRegistry = ReadAppPath(browser.BrowserExe);
            if (fromRegistry != null)
            {
                return fromRegistry;
            }

            return browser.FallbackPaths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
        }

        /// <summary>
        /// 从注册表 App Paths 读浏览器安装位置，能覆盖装在非默认目录的情况
        /// </summary>
        private static string? ReadAppPath(string exeName)
        {
            var subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName;
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var key = root.OpenSubKey(subKey);
                    if (key?.GetValue(null) is string path && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (Exception)
                {
                    // 读不到就当没装
                }
            }

            return null;
        }

        /// <summary>
        /// 找现成的驱动：先看本程序的驱动目录（含手动放进去的），再看 Selenium 的默认缓存。
        /// 既支持 Selenium Manager 的多级缓存结构，也支持直接扔在根目录。
        /// </summary>
        private static string? FindLocalDriver(string driverExe)
        {
            foreach (var folder in new[] { PathUtils.DriverPath, DefaultSeleniumCachePath() })
            {
                try
                {
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    {
                        continue;
                    }

                    var found = Directory.GetFiles(folder, driverExe, SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (found != null)
                    {
                        return found;
                    }
                }
                catch (Exception)
                {
                    // 换下一个目录接着找
                }
            }

            return null;
        }

        /// <summary>
        /// Selenium Manager 不指定缓存目录时的默认位置，别的工具可能已经下过同一个驱动
        /// </summary>
        private static string DefaultSeleniumCachePath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? "" : Path.Combine(home, ".cache", "selenium");
        }

        /// <summary>
        /// 下载过程中 Selenium Manager 会建 sm.lock，进程被强杀会把它留下，
        /// 导致下次启动一直等这把永远不会释放的锁。
        /// </summary>
        private static void ClearStaleLocks()
        {
            try
            {
                if (!Directory.Exists(PathUtils.DriverPath))
                {
                    return;
                }

                foreach (var lockFile in Directory.GetFiles(PathUtils.DriverPath, "sm.lock", SearchOption.AllDirectories))
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(lockFile) < TimeSpan.FromMinutes(5))
                    {
                        //可能真有另一个实例在下载，别去动它
                        continue;
                    }

                    File.Delete(lockFile);
                    LogUtils.Log($"已清理残留的下载锁：{lockFile}");
                }
            }
            catch (Exception)
            {
                // 清不掉就算了，不影响主流程
            }
        }
    }
}
