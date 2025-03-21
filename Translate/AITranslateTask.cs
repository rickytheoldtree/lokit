using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Utils;
using System.Threading.Tasks;

namespace LocalizationTranslationWPF.Translate
{
    public static class AITranslateTask
    {
        public static async Task Run(MainWindow mainWindow)
        {
            mainWindow.SetUILock(true);
            await Task.Run(async () =>
            {
                var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
                if (dictConverter == null)
                {
                    LogUtils.LogError("字典转换器类型错误");
                    return;
                }
                var sourceLang = ConfigUtils.SourceLang;
                var targetLang = ConfigUtils.TargetLang;
                if (targetLang.Name == "All")
                {
                    foreach (var pair in PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value))
                    {
                        targetLang = pair;
                        if (!await TaskUtils.AITranslateTask(sourceLang, targetLang, dictConverter))
                        {
                            LogUtils.LogError($"翻译{targetLang.Name}失败");
                            continue;
                        }
                    }
                }
                else
                {
                    await TaskUtils.AITranslateTask(sourceLang, targetLang, dictConverter);
                }
            });
            mainWindow.SetUILock(false);
        }
    }
}
