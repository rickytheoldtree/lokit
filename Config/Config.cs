using System;
using LocalizationTranslationWPF.DictConverter;

namespace LocalizationTranslationWPF.Data
{
    [Serializable]
    public class Config
    {
        public WebDriverType webDriverType;
        public DictConverterType dictConverter;
        public int isoPairIndex;
        public TranslateMethod translateMethod;
        public string aiAPIKey = "输入你的apikey";
        public string aiEndpoint = "https://api.deepseek.com/chat/completions";
        public string aiModel = "deepseek-chat";
        public string aiPrompt = "你是休闲游戏的翻译官，你需要将游戏中的文本翻译成指定语言。";

        public enum WebDriverType
        {
            Edge,
            Chrome,
            FireFox,
        }

        public enum TranslateMethod
        {
            GoogleTranslate,
            AI,
        }
    }
}
