using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Utils;

namespace LocalizationTranslationWPF.Translate
{
    /// <summary>
    /// 各翻译器共用的流程：目标语言遍历、源文件读取、结果落盘。
    /// </summary>
    public static class TranslationJob
    {
        /// <summary>
        /// 把源语言的一个文件翻译到目标语言
        /// </summary>
        public delegate Task<bool> TranslateFile(Pair from, Pair to, IDictConverter.IDictConverter converter,
            CancellationToken token);

        /// <summary>
        /// 把整段源文本交给翻译服务，返回翻译后的整段文本
        /// </summary>
        public delegate Task<string> RequestTranslation(string sourceText, Pair to, CancellationToken token);

        /// <summary>
        /// 按“目标语言数量”配置遍历目标语言并逐个翻译
        /// </summary>
        /// <param name="delayBetween">批量翻译时每个语言之间的额外等待，单语言模式不生效</param>
        public static async Task RunAsync(IDictConverter.IDictConverter converter, TranslateFile translate,
            Func<Task>? delayBetween, CancellationToken token)
        {
            var sourceLang = ConfigUtils.SourceLang;
            var targetLang = ConfigUtils.TargetLang;
            var pairs = PairUtils.GetPairs(ConfigUtils.IsoPairIndex.Value);

            switch (ConfigUtils.TargetLangAmount.Value)
            {
                case Config.TargetLangAmount.All:
                    await TranslateRangeAsync(pairs, 0, sourceLang, converter, translate, delayBetween, token);
                    break;

                case Config.TargetLangAmount.Remain:
                {
                    var index = pairs.FindIndex(p => p.isoCode == targetLang.isoCode);
                    if (index == -1)
                    {
                        LogUtils.LogError("目标语言不在剩余语言列表中");
                        return;
                    }
                    await TranslateRangeAsync(pairs, index, sourceLang, converter, translate, delayBetween, token);
                    break;
                }

                case Config.TargetLangAmount.Single:
                default:
                    await translate(sourceLang, targetLang, converter, token);
                    break;
            }
        }

        private static async Task TranslateRangeAsync(List<Pair> pairs, int startIndex, Pair sourceLang,
            IDictConverter.IDictConverter converter, TranslateFile translate, Func<Task>? delayBetween,
            CancellationToken token)
        {
            for (var i = startIndex; i < pairs.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var targetLang = pairs[i];
                if (!await translate(sourceLang, targetLang, converter, token))
                {
                    LogUtils.LogError($"翻译{targetLang.Name}失败");
                }

                if (delayBetween != null)
                {
                    await delayBetween();
                }
            }
        }

        /// <summary>
        /// AI 类翻译器（整份 json 一次性丢给模型）的通用流程
        /// </summary>
        public static async Task<bool> TranslateWithAiAsync(Pair from, Pair to, IDictConverter.IDictConverter converter,
            RequestTranslation request, CancellationToken token)
        {
            if (!TryLoadSource(from, converter, out var originJson) || originJson == null)
            {
                return false;
            }

            if (CopyWhenSameLanguage(from, to, originJson, converter))
            {
                return true;
            }

            var result = await request(converter.Serialize(originJson), to, token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            Dictionary<string, string>? dic;
            try
            {
                if (!converter.TryDeserialize(result, out dic) || dic == null)
                {
                    LogUtils.LogError($"{from.Name}翻译失败，ai翻译结果格式错误");
                    return false;
                }
            }
            catch
            {
                LogUtils.LogError($"{from.Name}翻译失败，ai翻译结果格式错误");
                return false;
            }

            WriteOutput(to, dic, converter);
            return true;
        }

        /// <summary>
        /// 读取输入文件夹中的源语言文件，失败时已写好日志
        /// </summary>
        public static bool TryLoadSource(Pair from, IDictConverter.IDictConverter converter,
            out Dictionary<string, string>? source)
        {
            var originJsonPath = PathUtils.InputFile(from.Name);
            if (!File.Exists(originJsonPath))
            {
                LogUtils.LogError($"{originJsonPath}不存在");
                source = null;
                return false;
            }

            if (!IOUtils.TryInputJson(originJsonPath, converter, out source) || source == null)
            {
                LogUtils.LogError($"{originJsonPath}读取出错，检查项目标准");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 源语言与目标语言相同时直接原样输出，返回是否已处理
        /// </summary>
        public static bool CopyWhenSameLanguage(Pair from, Pair to, Dictionary<string, string> source,
            IDictConverter.IDictConverter converter)
        {
            if (from.isoCode != to.isoCode)
            {
                return false;
            }

            IOUtils.OutputJson(PathUtils.OutputFile(to.Name), source, converter);
            LogUtils.Log($"跳过翻译{to.Name}，因为与源语言相同");
            return true;
        }

        /// <summary>
        /// 写出翻译结果并记录日志
        /// </summary>
        public static void WriteOutput(Pair to, Dictionary<string, string> dict,
            IDictConverter.IDictConverter converter)
        {
            var path = PathUtils.OutputFile(to.Name);
            IOUtils.OutputJson(path, dict, converter);
            LogUtils.Log($"{to.Name}翻译完成，文件路径：{path}");
        }

        /// <summary>
        /// 用目标语言和待翻译文本填充用户配置的 prompt 模板；
        /// 模板里如果有多余的花括号导致格式化失败，则退回默认模板
        /// </summary>
        public static string FormatPrompt(string prompt, Pair to, string text)
        {
            //{0}是目标语言，{1}是待翻译内容，缺了{1}会导致发出去的请求没有正文
            if (string.IsNullOrWhiteSpace(prompt) || !prompt.Contains("{1}"))
            {
                LogUtils.LogError("Prompt无效（需要包含待翻译内容占位符{1}），本次使用默认Prompt");
                prompt = ConfigUtils.DefaultAIPrompt;
            }

            try
            {
                return string.Format(prompt, to.Name, text);
            }
            catch (FormatException)
            {
                LogUtils.LogError("Prompt格式有误（请检查花括号），本次使用默认Prompt");
                return string.Format(ConfigUtils.DefaultAIPrompt, to.Name, text);
            }
        }
    }
}
