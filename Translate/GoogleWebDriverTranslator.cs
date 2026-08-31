using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Utils;
using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalizationTranslationWPF.DictConverter;

namespace LocalizationTranslationWPF.Translate
{
    public class GoogleWebDriverTranslator : WebDriverTranslator
    {
        /// <summary>
        /// 谷歌翻译单次请求的文本长度上限
        /// </summary>
        private const int MaxRequestLength = 5000;

        private const string Separator = "-----------------------";

        private GoogleWebDriverTranslator(Config.WebDriverType type, string driverPath) : base(type, driverPath)
        {
        }

        public static async Task<bool> Run(CancellationToken token, bool includeChanged = false)
        {
            return await Task.Run(async () =>
            {
                var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
                if (dictConverter == null)
                {
                    throw new Exception("字典转换器类型错误");
                }

                var driver = await WebDriverProvider.PrepareAsync(ConfigUtils.WebDriver.Value, token);
                using var translator = new GoogleWebDriverTranslator(driver.Type, driver.DriverPath);
                return await TranslationJob.RunAsync(dictConverter, translator.TranslateAsync, null, token, includeChanged);
            }, token);
        }

        private async Task<Dictionary<string, string>?> TranslateAsync(Pair from, Pair to,
            Dictionary<string, string> originJson, IDictConverter.IDictConverter converter, CancellationToken token)
        {
            var values = new List<string>();
            var sb = new StringBuilder();
            foreach (var d in originJson)
            {
                var newText = $"{Separator}%0A{GetUrlSafeString(d.Value.Trim())}%0A";
                if (newText.Length > MaxRequestLength)
                {
                    LogUtils.LogError($"单个文本长度超过{MaxRequestLength}，无法翻译：{d.Key}；请自行切分后重新尝试");
                    return null;
                }

                //超长时先把已攒下的一批发出去
                if (sb.Length + newText.Length > MaxRequestLength)
                {
                    if (!await Translate(sb.ToString(), from.isoCode, to.isoCode, values, token))
                    {
                        return null;
                    }

                    sb.Clear();
                }

                sb.Append(newText);
            }

            if (sb.Length > 0)
            {
                if (!await Translate(sb.ToString(), from.isoCode, to.isoCode, values, token))
                {
                    return null;
                }
            }

            var keys = originJson.Keys.ToList();
            if (keys.Count != values.Count)
            {
                LogUtils.LogError("翻译后数量与原文本数量不一致");
                return null;
            }

            var dic = new Dictionary<string, string>();
            for (var i = 0; i < keys.Count; i++)
            {
                dic.Add(keys[i], values[i]);
            }

            return dic;
        }

        private async Task<bool> Translate(string txt, string from, string to, List<string> results,
            CancellationToken token)
        {
            var url = $"https://translate.google.com/?hl=en&sl={from}" +
                      $"&tl={to}" +
                      $"&text={txt}" +
                      "&op=translate";

            if (!ToUrl(url)) return false;

            var btn = await FindElementBy(By.CssSelector("button[aria-label='Copy translation']"), 100, token);
            if (btn == null) return false;
            btn.Click();

            //访问Windows粘贴板
            var text = ThreadUtils.GetClipboard();
            if (!TaskUtils.Txt2StrList(text, out var values))
            {
                return false;
            }

            results.AddRange(values);
            return true;
        }

        private static string GetUrlSafeString(string str)
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
