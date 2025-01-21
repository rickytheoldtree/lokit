using LocalizationTranslationWPF.DictConverter;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Translate;
using IDictConverter;
using System.Threading.Tasks;

namespace LocalizationTranslationWPF.Helper
{
    public static class TaskHelper
    {
        public static bool Txt2StrList(string text, out List<string> values)
        {
            //从第一个-开始到第一个不为-结束
            var startIndex = text.IndexOf('-');
            if (startIndex == -1)
            {
                values = new List<string>();
                Debug.WriteLine("找不到分隔符");
                return false;
            }
            var length = 0;
            while (text.Length > startIndex + length && text[startIndex + length] == '-')
            {
                length++;
            }
            var splitString = text.Substring(startIndex, length);
            values = text.Split(new[] { splitString }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s)).ToList();
            return true;
        }

        public static async Task<bool> TranslateTask(Pair from, Pair to, IDictConverter.IDictConverter converter)
        {
            GoogleTranslator translator;
            try
            {
                translator = GoogleTranslator.GetInstance(ConfigHelper.WebDriver.Value);
            }
            catch
            {
                LogHelper.LogError($"{ConfigHelper.WebDriver.Value} 无法正常工作，请下载对应浏览器版本的WebDriver到根目录下的web-drivers里(搜索\"WebDriver {ConfigHelper.WebDriver.Value}\")");
                return false;
            }
            if (from.isoCode == to.isoCode)
            {
                LogHelper.Log($"跳过翻译{to.Name}，因为与源语言相同");
                return true;
            }

            var originJsonPath = $"{PathHelper.InputPath}\\{from.Name}.json";
            if (!File.Exists(originJsonPath))
            {
                LogHelper.LogError($"{PathHelper.InputPath}\\{from.Name}.json不存在");
                return false;
            }
            var sb = new StringBuilder();
            if (!TryInputJson(originJsonPath, converter, out var originJson) || originJson == null)
            {
                LogHelper.LogError($"{PathHelper.InputPath}\\{from.Name}.json读取出错，检查项目标准");
                return false;
            }
            var values = new List<string>();
            foreach (var d in originJson)
            {
                var newText = $"-----------------------%0A{GoogleTranslator.GetUrlSafeString(d.Value.Trim())}%0A";
                if (newText.Length > 5000)
                {
                    LogHelper.LogError($"单个文本长度超过5000，无法翻译：{d.Key}；请自行切分后重新尝试");
                    return false;
                }
                if (sb.Length + newText.Length <= 5000)
                {
                    sb.Append(newText);
                }
                else
                {
                    if (!await translator.Translate(sb.ToString(), from.isoCode, to.isoCode, (result) => { values.AddRange(result); }))
                    {
                        return false;
                    }
                    sb.Clear();
                    sb.Append(newText);
                }
            }
            if (sb.Length > 0)
            {
                if (!await translator.Translate(sb.ToString(), from.isoCode, to.isoCode, (result) => { values.AddRange(result); }))
                {
                    return false;
                }
            }
            var dic = new Dictionary<string, string>();
            var keys = originJson.Keys.ToList();
            if (keys.Count != values.Count)
            {
                LogHelper.LogError("翻译后数量与原文本数量不一致");
                return false;
            }
            for (var i = 0; i < keys.Count; i++)
            {
                dic.Add(keys[i], values[i]);
            }
            if (!Directory.Exists(PathHelper.OutputPath))
            {
                Directory.CreateDirectory(PathHelper.OutputPath);
            }
            OutputJson($"{PathHelper.OutputPath}\\{to.Name}.json", dic, converter);
            LogHelper.Log($"{to.Name}翻译完成，文件路径：{PathHelper.OutputPath}\\{to.Name}.json");
            return true;
        }

        private static void OutputJson(string path, Dictionary<string, string> dic, IDictConverter.IDictConverter converter)
        {
            File.WriteAllText(path, converter.Serialize(dic));
        }

        private static bool TryInputJson(string originJsonPath, IDictConverter.IDictConverter converter, out Dictionary<string, string>? dict)
        {
            try
            {
                return converter.TryDeserialize(File.ReadAllText(originJsonPath), out dict);
            }
            catch
            {
                dict = null;
                return false;
            }
            
        }

        public static bool CombineTask(Pair pair)
        {
            var sourcePath = $"{PathHelper.CombineSourcePath}\\{pair.Name}.json";
            var newPath = $"{PathHelper.OutputPath}\\{pair.Name}.json";
            var dictConverter = DictConverterHelper.GetConverterInstance(ConfigHelper.DictConverter.Value);
            if(dictConverter == null)
            {
                LogHelper.LogError("无法找到转换器");
                return false;
            }
            if(!TryInputJson(sourcePath, dictConverter, out var sourceDict) || sourceDict == null)
            {
                LogHelper.LogError($"读取{sourcePath}失败");
                return false;
            }
            if (!TryInputJson(newPath, dictConverter, out var newDict) || newDict == null)
            {
                LogHelper.LogError($"读取{newPath}失败");
                return false;
            }
            //将newDict合并到sourceDict，如果有重复的key，以newDict为准
            foreach (var kv in newDict)
            {
                sourceDict[kv.Key] = kv.Value;
            }
            OutputJson($"{PathHelper.CombineOutputPath}\\{pair.Name}.json", sourceDict, dictConverter);
            LogHelper.Log($"{pair.Name}合并完成，文件路径：{PathHelper.OutputPath}\\{pair.Name}.json");
            return true;
        }
    }
}
