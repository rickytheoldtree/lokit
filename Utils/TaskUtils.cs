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