using LocalizationTranslationWPF.DictConverter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Translate;
using System.Threading.Tasks;

namespace LocalizationTranslationWPF.Utils
{
    public static class TaskUtils
    {
        public static bool Txt2StrList(string text, out List<string> values)
        {
            //从第一个-开始到第一个不为-结束
            var startIndex = text.IndexOf('-');
            if (startIndex == -1)
            {
                values = new List<string>();
                LogUtils.LogError("找不到分隔符");
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

        public static async Task<bool> GoogleTranslateTask(GoogleTranslator translator, Pair from, Pair to, IDictConverter.IDictConverter converter, CancellationToken token)
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
                var newText = $"-----------------------%0A{GoogleTranslator.GetUrlSafeString(d.Value.Trim())}%0A";
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
                    if (!await translator.Translate(sb.ToString(), from.isoCode, to.isoCode,
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
                if (!await translator.Translate(sb.ToString(), from.isoCode, to.isoCode,
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

        public static async Task<bool> AITranslateTask(Pair from, Pair to, IDictConverter.IDictConverter converter, CancellationToken token)
        {
            var translator = new AITranslator(ConfigUtils.Config.aiAPIKey, ConfigUtils.Config.aiEndpoint,
                ConfigUtils.Config.aiModel, ConfigUtils.Config.aiPrompt);

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

            string result = await translator.Translate(converter.Serialize(originJson), to, token);
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

        public static bool CombineTask(Pair pair)
        {
            var sourcePath = $"{PathUtils.CombineSourcePath}\\{pair.Name}.json";
            var newPath = $"{PathUtils.OutputPath}\\{pair.Name}.json";
            var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
            if (dictConverter == null)
            {
                LogUtils.LogError("无法找到转换器");
                return false;
            }

            if (!IOUtils.TryInputJson(sourcePath, dictConverter, out var sourceDict) || sourceDict == null)
            {
                LogUtils.LogError($"读取{sourcePath}失败");
                return false;
            }

            if (!IOUtils.TryInputJson(newPath, dictConverter, out var newDict) || newDict == null)
            {
                LogUtils.LogError($"读取{newPath}失败");
                return false;
            }

            //将newDict合并到sourceDict，如果有重复的key，以newDict为准
            foreach (var kv in newDict)
            {
                sourceDict[kv.Key] = kv.Value;
            }

            IOUtils.OutputJson($"{PathUtils.CombineOutputPath}\\{pair.Name}.json", sourceDict, dictConverter);
            LogUtils.Log($"{pair.Name}合并完成，文件路径：{PathUtils.OutputPath}\\{pair.Name}.json");
            return true;
        }
    }
}