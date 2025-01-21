using LocalizationTranslationWPF.Helper;
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
            var sourceLang = ConfigHelper.SourceLang;
            var targetLang = ConfigHelper.TargetLang;
            var sourcePath = PathHelper.InputPath + "\\" + sourceLang.Name + ".json";
            var targetPath = PathHelper.OutputPath + "\\" + targetLang.Name + ".json";
            if (!File.Exists(sourcePath))
            {
                LogHelper.LogError($"{sourcePath}不存在");
                return;
            }
            var sourceJson = File.ReadAllText(sourcePath);
            try
            {
                sourceJson = ChineseConverter.Convert(sourceJson, ChineseConversionDirection.SimplifiedToTraditional);
                File.WriteAllText(targetPath, sourceJson);
                LogHelper.Log($"翻译{sourceLang.Name}到{targetLang.Name}成功");
            }
            catch(Exception e)
            {
                LogHelper.LogError(e.Message);
            }
        }
    }
}
