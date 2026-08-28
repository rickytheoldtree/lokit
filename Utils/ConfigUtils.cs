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
        public const string DefaultAIPrompt = "你是休闲游戏的翻译官，你需要将游戏中的文本翻译成指定语言。" +
                                              "接下来你将翻译我提供的json格式文件，你需要翻译成纯文本形式的json，一定一定不要markdown代码块，否则对我来说没有意义!" +
                                              "如果翻译内容里出现了占位符，请务必保留，请确保翻译后的文本中也有这些标记，且数量不变。" +
                                              "请在最后用[end]结束你的翻译。" +
                                              "请将以下内容翻译为{0}对应的语言：{1}";

        public static Config Config => config ??= Load();
        private static Config? config;

        #region 可绑定配置项

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

        public static BindableProperty<string> DriverProxy { get; } = new();

        public static Pair SourceLang { get; private set; }

        public static Pair TargetLang { get; private set; }

        #endregion

        static ConfigUtils()
        {
            Bind(WebDriver, Config.webDriverType, value => Config.webDriverType = value);
            Bind(DictConverter, DictConverterUtils.GetConverterType(Config.dictConverter.type),
                value => Config.dictConverter = new DictConverterType(value));
            Bind(IsoPairIndex, Config.isoPairIndex, value => Config.isoPairIndex = value);
            Bind(TranslateMethod, Config.translateMethod, value => Config.translateMethod = value);
            Bind(TargetLangAmount, Config.targetLangAmount, value => Config.targetLangAmount = value);
            Bind(ApiEndpoint, Config.aiEndpoint, value => Config.aiEndpoint = value);
            Bind(ApiKey, Config.aiAPIKey, value => Config.aiAPIKey = value);
            Bind(AIModel, Config.aiModel, value => Config.aiModel = value);
            Bind(AIPrompt, Config.aiPrompt, value => Config.aiPrompt = value);
            Bind(DriverProxy, Config.driverProxy, value => Config.driverProxy = value);

            //源/目标语言只在本次运行内有效，不写入配置
            SourceLangIndex.SetWithoutInvoke(0);
            SourceLangIndex.Register(index => SourceLang = GetPair(index));

            TargetLangIndex.SetWithoutInvoke(0);
            TargetLangIndex.Register(index => TargetLang = GetPair(index));
        }

        /// <summary>
        /// 用配置里的值初始化（不触发回调），之后每次变更都写回配置并保存
        /// </summary>
        private static void Bind<T>(BindableProperty<T> property, T initialValue, Action<T> apply)
        {
            property.SetWithoutInvoke(initialValue);
            property.Register(value =>
            {
                apply(value);
                Save();
            });
        }

        private static Pair GetPair(int index)
        {
            var pairs = PairUtils.GetPairs(IsoPairIndex.Value);
            return index >= 0 && index < pairs.Count ? pairs[index] : default;
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(PathUtils.DataPath);
                File.WriteAllText(PathUtils.ConfigPath, JsonConvert.SerializeObject(Config));
            }
            catch (Exception e)
            {
                LogUtils.LogError($"保存配置失败：{e.Message}");
            }
        }

        private static Config Load()
        {
            if (!File.Exists(PathUtils.ConfigPath))
            {
                return new Config();
            }

            try
            {
                return JsonConvert.DeserializeObject<Config>(File.ReadAllText(PathUtils.ConfigPath)) ?? new Config();
            }
            catch (Exception)
            {
                return new Config();
            }
        }
    }
}
