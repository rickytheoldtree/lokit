using LocalizationTranslationWPF.Common;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using Newtonsoft.Json;
using System;
using System.IO;
using static LocalizationTranslationWPF.Data.Config;

namespace LocalizationTranslationWPF.Helper
{
    public static class ConfigHelper
    {
        public static Config Config => config ??= Load();
        private static Config? config;
        public static EventProperty<WebDriverType> WebDriver { get; } = new EventProperty<WebDriverType>(Config.webDriverType, type =>
        {
            Config.webDriverType = type;
            Save();
        });

        public static EventProperty<Type> DictConverter { get; }
            = new EventProperty<Type>(DictConverterHelper.GetConverterType(Config.dictConverter.type), type =>
            {
                Config.dictConverter = new DictConverterType(type);
                Save();
            });
        public static EventProperty<int> IsoPairIndex { get; } = new EventProperty<int>(Config.isoPairIndex, index =>
        {
            Config.isoPairIndex = index;
            Save();
        });
        public static EventProperty<int> SourceLangIndex { get; } = new EventProperty<int>(0, index =>
        {
            SourceLang = PairHelper.GetPairs(IsoPairIndex.Value)[index];
        });
        public static EventProperty<int> TargetLangIndex { get; } = new EventProperty<int>(0, index =>
        {
            TargetLang = PairHelper.GetPairsWithAll(IsoPairIndex.Value)[index];
        });
        public static Pair SourceLang { get; private set; }
        public static Pair TargetLang { get; private set; }
        private static void Save()
        {
            File.WriteAllText(PathHelper.ConfigPath, JsonConvert.SerializeObject(Config));
        }
        private static Config Load()
        {
            if (File.Exists(PathHelper.ConfigPath))
            {
                try
                {
                    var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(PathHelper.ConfigPath));
                    return config ?? new Config();
                }
                catch (Exception)
                {
                    return new Config();
                }
            }
            else
            {
                return new Config();
            }
        }
    }
}
