using LocalizationTranslationWPF.DictConverter;
using System;
using System.Collections.Generic;
using System.Linq;
using LocalizationTranslationWPF.Data;

namespace LocalizationTranslationWPF.Utils
{
    public static class TaskUtils
    {
        /// <summary>
        /// 按文本里第一段连续的“-”做分隔符，把整段文本拆成多条
        /// </summary>
        public static bool Txt2StrList(string text, out List<string> values)
        {
            values = new List<string>();

            //从第一个-开始到第一个不为-结束
            var startIndex = text.IndexOf('-');
            if (startIndex == -1)
            {
                LogUtils.LogError("找不到分隔符");
                return false;
            }

            var length = 0;
            while (text.Length > startIndex + length && text[startIndex + length] == '-')
            {
                length++;
            }

            var splitString = text.Substring(startIndex, length);
            values = text.Split(new[] { splitString }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            return true;
        }

    }
}
