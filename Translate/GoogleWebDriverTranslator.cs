using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Utils;
using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalizationTranslationWPF.DictConverter;

namespace LocalizationTranslationWPF.Translate
{
    public class GoogleWebDriverTranslator : WebDriverTranslator
    {
        private GoogleWebDriverTranslator(Config.WebDriverType type) : base(type)
        {
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
                using var translator = new GoogleWebDriverTranslator(ConfigUtils.WebDriver.Value);
                switch (targetLang.Name)
                {
                    case "All":
                    {
                        foreach (var pair in PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value))
                        {
                            token.ThrowIfCancellationRequested();
                            targetLang = pair;
                            if (!await translator.GoogleTranslateTask(sourceLang, targetLang, dictConverter,
                                    token))
                            {
                                LogUtils.LogError($"翻译{targetLang.Name}失败");
                            }
                        }

                        break;
                    }
                    default:
                        await translator.GoogleTranslateTask(sourceLang, targetLang, dictConverter, token);
                        break;
                }
            }, token);
        }

        private async Task<bool> GoogleTranslateTask(Pair from, Pair to, IDictConverter.IDictConverter converter, CancellationToken token)
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

            var sb = new StringBuilder();
            if (!IOUtils.TryInputJson(originJsonPath, converter, out var originJson) || originJson == null)
            {
                LogUtils.LogError($"{PathUtils.InputPath}\\{from.Name}.json读取出错，检查项目标准");

                return false;
            }

            var values = new List<string>();
            foreach (var d in originJson)
            {
                var newText = $"-----------------------%0A{GetUrlSafeString(d.Value.Trim())}%0A";
                if (newText.Length > 5000)
                {
                    LogUtils.LogError($"单个文本长度超过5000，无法翻译：{d.Key}；请自行切分后重新尝试");
                    return false;
                }

                if (sb.Length + newText.Length <= 5000)
                {
                    sb.Append(newText);
                }
                else
                {
                    if (!await Translate(sb.ToString(), from.isoCode, to.isoCode,
                            (result) => { values.AddRange(result); }, token))
                    {
                        return false;
                    }

                    sb.Clear();
                    sb.Append(newText);
                }
            }

            if (sb.Length > 0)
            {
                if (!await Translate(sb.ToString(), from.isoCode, to.isoCode,
                        (result) => { values.AddRange(result); }, token))
                {
                    return false;
                }
            }

            var dic = new Dictionary<string, string>();
            var keys = originJson.Keys.ToList();
            if (keys.Count != values.Count)
            {
                LogUtils.LogError("翻译后数量与原文本数量不一致");
                return false;
            }

            for (var i = 0; i < keys.Count; i++)
            {
                dic.Add(keys[i], values[i]);
            }

            if (!Directory.Exists(PathUtils.OutputPath))
            {
                Directory.CreateDirectory(PathUtils.OutputPath);
            }

            IOUtils.OutputJson($"{PathUtils.OutputPath}\\{to.Name}.json", dic, converter);
            LogUtils.Log($"{to.Name}翻译完成，文件路径：{PathUtils.OutputPath}\\{to.Name}.json");
            return true;
        }
        private async Task<bool> Translate(string txt, string from, string to, Action<List<string>> callback,
            CancellationToken token)
        {
            var url = $"https://translate.google.com/?hl=en&sl={from}" +
                  $"&tl={to}" +
                  $"&text={txt}" +
                  "&op=translate";
            
            if(!ToUrl(url)) return false;

            var btn = await FindElementBy(By.CssSelector("button[aria-label='Copy translation']"), 100, token);
            if (btn == null)
            {
                LogUtils.LogError("找不到按钮");
                return false;
            }
            
            await Task.Delay(500, token);

            //访问Windows粘贴板
            var text = ThreadUtils.GetClipboard();
            if (!TaskUtils.Txt2StrList(text, out var values))
            {
                return false;
            }

            callback.Invoke(values);
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