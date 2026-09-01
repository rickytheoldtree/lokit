using LocalizationTranslationWPF.DictConverter;

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
            "示例：\nEnglish,en\nChineseSimplified,zh-CN\n创建项目时使用的语言文件名与 ISO 对应表；已打开项目的语言表保持固定。";

        public static string SourceLangToolTip => "使用当前项目的源语言工作副本；源语言在创建项目时确定。";

        public static string TargetLangToolTip => "译文保存到项目工作副本，检查后通过“发布”写回正式目录。";
    }
}
