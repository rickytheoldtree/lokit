using LocalizationTranslationWPF.Data;
using System;
using System.Collections.Generic;

namespace LocalizationTranslationWPF.Utils
{
    public static class PairUtils
    {
        private static readonly List<Pair> FallBackList = new()
        {
            new Pair("English", "en"),
            new Pair("ChineseSimplified", "zh-CN")
        };
        private static readonly Pair AllPair = new("All", "All");
        public static Dictionary<string, List<Pair>> PairsCache { get; } = LoadAll();
        public static List<Pair> GetPairs(int index)
        {
            int count = 0;
            foreach (var item in PairsCache)
            {
                if (count == index)
                {
                    return item.Value;
                }
                count++;
            }
            return FallBackList;
        }
        private static Dictionary<string, List<Pair>> LoadAll()
        {
            var dict = new Dictionary<string, List<Pair>>();
            var path = PathUtils.IsoPath;
            //遍历path下的所有文件
            var files = System.IO.Directory.GetFiles(path);
            foreach (var file in files)
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(file);
                var str = System.IO.File.ReadAllText(file);
                var pairs = Load(str);
                dict.Add(name, pairs);
            }
            return dict;
        }
        private static List<Pair> Load(string str)
        {
            var pairs = new List<Pair>();
            var lines = str.Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if(parts.Length < 2) continue;
                var pair = new Pair(parts[0].Trim(), parts[1].Trim());
                pairs.Add(pair);
            }
            return pairs;
        }
    }
}
