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
                if (targetLang.Name == "All")
                {
                    foreach (var pair in PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value))
                    {
                        targetLang = pair;
                        if (!await TaskUtils.GoogleTranslateTask(sourceLang, targetLang, dictConverter, token))
                        {
                            LogUtils.LogError($"翻译{targetLang.Name}失败");
                        }

                        if (token.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    await TaskUtils.GoogleTranslateTask(sourceLang, targetLang, dictConverter, token);
                }
            }, token);
        }
    }
}
