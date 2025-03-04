using LocalizationTranslationWPF.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;

namespace LocalizationTranslationWPF.Translate
{
    internal class Simplified2TraditionalTranslator
    {
        internal static void Translate()
        {
            var sourceLang = ConfigUtils.SourceLang;
            var targetLang = ConfigUtils.TargetLang;
            var sourcePath = PathUtils.InputPath + "\\" + sourceLang.Name + ".json";
            var targetPath = PathUtils.OutputPath + "\\" + targetLang.Name + ".json";
            if (!File.Exists(sourcePath))
            {
                LogUtils.LogError($"{sourcePath}不存在");
                return;
            }
            var sourceJson = File.ReadAllText(sourcePath);
            try
            {
                sourceJson = ChineseConverter.Convert(sourceJson, ChineseConversionDirection.SimplifiedToTraditional);
                File.WriteAllText(targetPath, sourceJson);
                LogUtils.Log($"翻译{sourceLang.Name}到{targetLang.Name}成功");
            }
            catch(Exception e)
            {
                LogUtils.LogError(e.Message);
            }
        }
    }
}
