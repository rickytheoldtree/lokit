using LocalizationTranslationWPF.Utils;
using System;
using System.IO;
using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;

namespace LocalizationTranslationWPF.Translate
{
    public static class Simplified2TraditionalTranslator
    {
        public static void Translate()
        {
            var sourceLang = ConfigUtils.SourceLang;
            var targetLang = ConfigUtils.TargetLang;
            if (string.IsNullOrEmpty(sourceLang.Name) || string.IsNullOrEmpty(targetLang.Name))
            {
                LogUtils.LogError("未选择源语言或目标语言");
                return;
            }

            var sourcePath = PathUtils.InputFile(sourceLang.Name);
            var targetPath = PathUtils.OutputFile(targetLang.Name);
            if (!File.Exists(sourcePath))
            {
                LogUtils.LogError($"{sourcePath}不存在");
                return;
            }

            try
            {
                var json = ChineseConverter.Convert(File.ReadAllText(sourcePath),
                    ChineseConversionDirection.SimplifiedToTraditional);
                Directory.CreateDirectory(PathUtils.OutputPath);
                File.WriteAllText(targetPath, json);
                LogUtils.Log($"翻译{sourceLang.Name}到{targetLang.Name}成功");
            }
            catch (Exception e)
            {
                LogUtils.LogError(e.Message);
            }
        }
    }
}
