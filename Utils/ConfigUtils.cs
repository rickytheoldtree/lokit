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

        static ConfigUtils()
        {
            WebDriver.SetWithoutInvoke(Config.webDriverType);
            WebDriver.Register(type =>
            {
                Config.webDriverType = type;
                Save();
            });

            DictConverter.SetWithoutInvoke(DictConverterUtils.GetConverterType(Config.dictConverter.type));
            DictConverter.Register(type =>
            {
                Config.dictConverter = new DictConverterType(type);
                Save();
            });

            IsoPairIndex.SetWithoutInvoke(Config.isoPairIndex);
            IsoPairIndex.Register(index =>
            {
                Config.isoPairIndex = index;
                Save();
            });

            SourceLangIndex.SetWithoutInvoke(0);
            SourceLangIndex.Register(index => { SourceLang = PairUtils.GetPairs(IsoPairIndex.Value)[index]; });

            TargetLangIndex.SetWithoutInvoke(0);
            TargetLangIndex.Register(index => { TargetLang = PairUtils.GetPairsWithAll(IsoPairIndex.Value)[index]; });

            TranslateMethod.SetWithoutInvoke(Config.translateMethod);
            TranslateMethod.Register(method =>
            {
                Config.translateMethod = method;
                Save();
            });

            ApiEndpoint.SetWithoutInvoke(Config.aiEndpoint);
            ApiEndpoint.Register(value =>
            {
                Config.aiEndpoint = value;
                Save();
            });

            ApiKey.SetWithoutInvoke(Config.aiAPIKey);
            ApiKey.Register(value =>
            {
                Config.aiAPIKey = value;
                Save();
            });

            AIModel.SetWithoutInvoke(Config.aiModel);
            AIModel.Register(value =>
            {
                Config.aiModel = value;
                Save();
            });

            AIPrompt.SetWithoutInvoke(Config.aiPrompt);
            AIPrompt.Register(value =>
            {
                Config.aiPrompt = value;
                Save();
            });
        }

        public static BindableProperty<WebDriverType> WebDriver { get; } = new();

        public static BindableProperty<Type> DictConverter { get; } = new();

        public static BindableProperty<int> IsoPairIndex { get; } = new();

        public static BindableProperty<int> SourceLangIndex { get; } = new();

        public static BindableProperty<int> TargetLangIndex { get; } = new();

        public static BindableProperty<TranslateMethod> TranslateMethod { get; } = new();

        public static BindableProperty<string> ApiEndpoint { get; } = new();

        public static BindableProperty<string> ApiKey { get; } = new();

        public static BindableProperty<string> AIModel { get; } = new();

        public static BindableProperty<string> AIPrompt { get; } = new();

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