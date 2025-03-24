using LocalizationTranslationWPF.DictConverter;
using System.Collections.Generic;

namespace LocalizationTranslationWPF.Utils
{
    public static class ToolTipUtils
    {
        private static readonly Dictionary<string, string> SampleDict = new Dictionary<string, string>()
        {
            {"key1","value1" },
            {"key2","value2" },
        };
        public static string DictConverterToolTip
        {
            get
            {
                var converter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
                if (converter == null)
                {
                    return "无法找到转换器";
                }
                return $"示例：\n{converter.Serialize(SampleDict)}";
            }
        }

        public static string WebDriverToolTip => $"选择浏览器驱动程序，通常只需要最新版的浏览器即可，如果无法正常启动，请下载对应\"WebDriver\"到{PathUtils.DriverPath}";
        public static string IsoPairToolTip => "示例：\nEnglish,en\nChineseSimplified,zh-CN\n该配置文件为输出/入文件名与ISO对应表";
        public static string SourceLangToolTip => $"将以{PathUtils.InputPath}\\{ConfigUtils.SourceLang.Name}.json 为翻译源";
        public static string TargetLangToolTip => $"翻译后的文件将输出到{PathUtils.OutputPath}";
    }
}
