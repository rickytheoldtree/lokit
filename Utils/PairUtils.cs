using LocalizationTranslationWPF.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LocalizationTranslationWPF.Utils
{
    public static class PairUtils
    {
        private static readonly List<Pair> FallBackList = new()
        {
            new Pair("English", "en"),
            new Pair("ChineseSimplified", "zh-CN")
        };

        public static Dictionary<string, List<Pair>> PairsCache { get; } = LoadAll();

        /// <summary>
        /// 取第 index 份“文件名-ISO”对应表，越界时回退到内置表
        /// </summary>
        public static List<Pair> GetPairs(int index)
        {
            if (index < 0 || index >= PairsCache.Count)
            {
                return FallBackList;
            }
            return PairsCache.Values.ElementAt(index);
        }

        private static Dictionary<string, List<Pair>> LoadAll()
        {
            var dict = new Dictionary<string, List<Pair>>();
            var path = PathUtils.IsoPath;
            if (!Directory.Exists(path))
            {
                return dict;
            }

            //遍历path下的所有文件
            foreach (var file in Directory.GetFiles(path))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (dict.ContainsKey(name))
                {
                    continue;
                }
                try
                {
                    dict.Add(name, Load(File.ReadAllText(file)));
                }
                catch (Exception e)
                {
                    LogUtils.LogError($"读取{file}失败：{e.Message}");
                }
            }
            return dict;
        }

        private static List<Pair> Load(string str)
        {
            var pairs = new List<Pair>();
            var lines = str.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                pairs.Add(new Pair(parts[0].Trim(), parts[1].Trim()));
            }
            return pairs;
        }
    }
}
