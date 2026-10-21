using Lokit.Data;
using Lokit.DictConverter;
using Lokit.Translate;
using Lokit.Utils;

namespace Lokit;

public partial class MainWindow
{
    private Task<bool> RunManualTranslationAsync(CancellationToken token)
    {
        var converter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value)
                        ?? throw new InvalidOperationException("字典转换器类型错误");
        var prompt = ConfigUtils.AIPrompt.Value;
        return TranslationJob.RunAsync(converter, RequestManualTranslationAsync, null, token);

        Task<Dictionary<string, string>?> RequestManualTranslationAsync(Pair from, Pair to,
            Dictionary<string, string> source, IDictConverter.IDictConverter activeConverter, CancellationToken requestToken)
        {
            var text = TranslationJob.FormatPrompt(prompt, to, activeConverter.Serialize(source));
            var window = new ManualTranslationWindow(
                $"{from.Name} → {to.Name} · {source.Count} 条待翻译文案", text,
                response => TranslationJob.ParseAiResponse(source, activeConverter, response))
            {
                Owner = this,
            };
            LogUtils.Log($"等待手动 AI 回复：{to.Name}。请复制提示词，并粘贴完整回复。");
            return window.ShowAsync(requestToken);
        }
    }
}
