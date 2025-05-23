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
        public const string DefaultAIPrompt = "你是休闲游戏的翻译官，你需要将游戏中的文本翻译成指定语言。" +
                                              "接下来你将翻译我提供的json格式文件，你需要翻译成纯文本形式的json，一定一定不要markdown代码块，否则对我来说没有意义!" +
                                              "如果翻译内容里出现了占位符，请务必保留，请确保翻译后的文本中也有这些标记，且数量不变。" +
                                              "请在最后用[end]结束你的翻译。" +
                                              "请将以下内容翻译为{0}对应的语言：{1}";

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
            TargetLangIndex.Register(index => { TargetLang = PairUtils.GetPairs(IsoPairIndex.Value)[index]; });

            TranslateMethod.SetWithoutInvoke(Config.translateMethod);
            TranslateMethod.Register(method =>
            {
                Config.translateMethod = method;
                Save();
            });
            
            TargetLangAmount.SetWithoutInvoke(Config.targetLangAmount);
            TargetLangAmount.Register(amount =>
            {
                Config.targetLangAmount = amount;
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
        
        public static BindableProperty<TargetLangAmount> TargetLangAmount { get; } = new();

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
                    var c = JsonConvert.DeserializeObject<Config>(File.ReadAllText(PathUtils.ConfigPath));
                    return c ?? new Config();
                }
                catch (Exception)
                {
                    return new Config();
                }
            }
            return new Config();
        }
    }
}