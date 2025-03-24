using System;
using System.Threading;
using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Utils;
using System.Threading.Tasks;

namespace LocalizationTranslationWPF.Translate
{
    public static class GoogleTranslateTask
    {
        public static async Task Run(CancellationToken token)
        {
            await Task.Run(async () =>
            {
                var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
                if (dictConverter == null)
                {
                    throw new Exception("字典转换器类型错误");
                }

                var sourceLang = ConfigUtils.SourceLang;
                var targetLang = ConfigUtils.TargetLang;
                using var translator = new GoogleTranslator(ConfigUtils.WebDriver.Value);
                switch (targetLang.Name)
                {
                    case "All":
                    {
                        foreach (var pair in PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value))
                        {
                            token.ThrowIfCancellationRequested();
                            targetLang = pair;
                            if (!await TaskUtils.GoogleTranslateTask(translator, sourceLang, targetLang, dictConverter,
                                    token))
                            {
                                LogUtils.LogError($"翻译{targetLang.Name}失败");
                            }
                        }

                        break;
                    }
                    default:
                        await TaskUtils.GoogleTranslateTask(translator, sourceLang, targetLang, dictConverter, token);
                        break;
                }
            }, token);
        }
    }
}