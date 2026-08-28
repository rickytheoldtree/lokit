using LocalizationTranslationWPF.DictConverter;
using System.Collections.Generic;

namespace LocalizationTranslationWPF.Utils
{
    public static class ToolTipUtils
    {
        private static readonly Dictionary<string, string> SampleDict = new()
        {
            { "key1", "value1" },
            { "key2", "value2" },
        };

        public static string DictConverterToolTip
        {
            get
            {
                var converter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
                return converter == null ? "无法找到转换器" : $"示例：\n{converter.Serialize(SampleDict)}";
            }
        }

        public static string WebDriverToolTip =>
            "「自动」会挑选本机已安装的浏览器（优先 Edge）。\n" +
            $"驱动由程序按浏览器版本自动下载并缓存到 {PathUtils.DriverPath}，无需手动准备。";

        public static string IsoPairToolTip =>
            "示例：\nEnglish,en\nChineseSimplified,zh-CN\n该配置文件为输出/入文件名与ISO对应表";

        public static string SourceLangToolTip => $"将以{PathUtils.InputFile(ConfigUtils.SourceLang.Name)} 为翻译源";

        public static string TargetLangToolTip => $"翻译后的文件将输出到{PathUtils.OutputPath}";
    }
}
