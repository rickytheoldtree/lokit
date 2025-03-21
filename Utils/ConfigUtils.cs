using LocalizationTranslationWPF.Common;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using Newtonsoft.Json;
using System;
using System.IO;
using static LocalizationTranslationWPF.Data.Config;

namespace LocalizationTranslationWPF.Utils
{
    public static class ConfigUtils
    {
        public static Config Config => config ??= Load();
        private static Config? config;
        public static EventProperty<WebDriverType> WebDriver { get; } = new EventProperty<WebDriverType>(Config.webDriverType, type =>
        {
            Config.webDriverType = type;
            Save();
        });

        public static EventProperty<Type> DictConverter { get; }
            = new EventProperty<Type>(DictConverterUtils.GetConverterType(Config.dictConverter.type), type =>
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
            SourceLang = PairUtils.GetPairs(IsoPairIndex.Value)[index];
        });
        public static EventProperty<int> TargetLangIndex { get; } = new EventProperty<int>(0, index =>
        {
            TargetLang = PairUtils.GetPairsWithAll(IsoPairIndex.Value)[index];
        });
        public static EventProperty<TranslateMethod> TranslateMethod { get; } = new EventProperty<TranslateMethod>(Config.translateMethod, method =>
        {
            Config.translateMethod = method;
            Save();
        });

        public static EventProperty<string> ApiEndpoint { get; } = new EventProperty<string>(Config.aiEndpoint, value =>
        {
            Config.aiEndpoint = value;
            Save();
        });
        public static EventProperty<string> ApiKey { get; } = new EventProperty<string>(Config.aiAPIKey, value =>
        {
            Config.aiAPIKey = value;
            Save();
        });
        public static EventProperty<string> AIModel { get; } = new EventProperty<string>(Config.aiModel, value =>
        {
            Config.aiModel = value;
            Save();
        });
        public static Pair SourceLang { get; private set; }
        public static Pair TargetLang { get; private set; }
        private static void Save()
        {
            File.WriteAllText(PathUtils.ConfigPath, JsonConvert.SerializeObject(Config));
        }
        private static Config Load()
        {
            if (File.Exists(PathUtils.ConfigPath))
            {
                try
                {
                    var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(PathUtils.ConfigPath));
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
