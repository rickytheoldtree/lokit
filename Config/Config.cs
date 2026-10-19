using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Utils;

#pragma warning disable CA1051, CA1708, CA1720 // Names and fields are persisted in existing config.json files.

namespace LocalizationTranslationWPF.Data
{
    [Serializable]
    public class Config
    {
        public WebDriverType webDriverType = WebDriverType.Auto;
        public DictConverterType dictConverter;
        public int isoPairIndex;
        public TranslateMethod translateMethod;
        public TargetLangAmount targetLangAmount;
        public AppThemeMode appThemeMode = AppThemeMode.System;
        public string aiAPIKey = "";
        public string aiEndpoint = "https://api.deepseek.com/chat/completions";
        public string aiModel = "deepseek-chat";
        public string lastProject = "";

        public string aiPrompt = ConfigUtils.DefaultAIPrompt;

        /// <summary>
        /// 下载浏览器驱动时使用的代理，留空表示直连
        /// </summary>
        public string driverProxy = "";

        public enum WebDriverType
        {
            //数值是写进配置文件的，只能往后追加，不能重排
            Edge = 0,
            Chrome = 1,
            FireFox = 2,
            Auto = 3,
        }

        public enum TranslateMethod
        {
            //数值是写进配置文件的，只能往后追加，不能重排
            GoogleTranslate,
            DeepSeek,
            DeepSeekWithDeepThinking,
            AIApi,
            DeepL,
            ManualAI,
        }

        public enum TargetLangAmount
        {
            Single = 0,
            All = 2,
        }

        public enum AppThemeMode
        {
            // Values are persisted; append new modes instead of reordering.
            System = 0,
            Light = 1,
            Dark = 2,
        }
    }
}

#pragma warning restore CA1051, CA1708, CA1720
