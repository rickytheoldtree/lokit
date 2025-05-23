using System;
using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Utils;

namespace LocalizationTranslationWPF.Data
{
    [Serializable]
    public class Config
    {
        public WebDriverType webDriverType;
        public DictConverterType dictConverter;
        public int isoPairIndex;
        public TranslateMethod translateMethod;
        public TargetLangAmount targetLangAmount;
        public string aiAPIKey = "输入你的apikey";
        public string aiEndpoint = "https://api.deepseek.com/chat/completions";
        public string aiModel = "deepseek-chat";

        public string aiPrompt = ConfigUtils.DefaultAIPrompt;

        public enum WebDriverType
        {
            Edge,
            Chrome,
            FireFox,
        }

        public enum TranslateMethod
        {
            GoogleTranslate,
            DeepSeek,
            DeepSeekWithDeepThinking,
            AIApi,
        }

        public enum TargetLangAmount
        {
            Single,
            Remain,
            All,
        }
    }
}