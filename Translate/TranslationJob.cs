using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Projects;
using LocalizationTranslationWPF.Utils;

namespace LocalizationTranslationWPF.Translate
{
    /// <summary>Owns source snapshots and persistence; providers never access language files.</summary>
    public static class TranslationJob
    {
        private static readonly CompositeFormat DefaultPromptFormat =
            CompositeFormat.Parse(ConfigUtils.DefaultAIPrompt);

        public delegate Task<Dictionary<string, string>?> TranslateFile(Pair from, Pair to,
            Dictionary<string, string> source, IDictConverter.IDictConverter converter, CancellationToken token);
        public delegate Task<string> RequestTranslation(string sourceText, Pair to, CancellationToken token);

        public static async Task<bool> RunAsync(IDictConverter.IDictConverter converter, TranslateFile translate,
            Func<Task>? delayBetween, CancellationToken token)
        {
            var workspace = ProjectContext.Current ?? throw new InvalidOperationException("请先创建或打开项目。");
            var sourceLanguage = workspace.SourceLanguage;
            var targets = ConfigUtils.TargetLangAmount.Value == Config.TargetLangAmount.Single
                ? workspace.Pairs.Where(p => p.Name == ConfigUtils.TargetLang.Name).ToList()
                : workspace.Pairs.Where(p => p.Name != sourceLanguage.Name).ToList();
            if (targets.Count == 0) throw new InvalidOperationException("请选择目标语言。");
            var snapshot = workspace.ReadLanguage(sourceLanguage.Name);
            if (snapshot.Count == 0) throw new InvalidOperationException("源语言没有内容，请先新增文案。");
            var succeeded = true;
            for (var i = 0; i < targets.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var target = targets[i];
                try
                {
                    if (target.Name == sourceLanguage.Name) continue;
                    var input = workspace.PrepareTranslation(target.Name, snapshot);
                    if (input.Source.Count == 0)
                    {
                        LogUtils.Log($"{target.Name} 没有待翻译条目，已保留现有译文。");
                        continue;
                    }
                    LogUtils.Log($"正在翻译 {target.Name}：{input.Source.Count} 条。");
                    var result = await translate(sourceLanguage, target, new(input.Source), converter, token);
                    token.ThrowIfCancellationRequested();
                    if (result == null) throw new InvalidOperationException("服务未返回有效译文。");
                    workspace.ApplyTranslation(input, result);
                    LogUtils.Log($"{target.Name} 已保存到工作副本，可在编辑页检查后发布。");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    succeeded = false;
                    LogUtils.LogError($"翻译 {target.Name} 失败：{e.Message}");
                }
                if (delayBetween != null && i < targets.Count - 1) await delayBetween();
            }
            return succeeded;
        }

        public static async Task<Dictionary<string, string>?> TranslateWithAiAsync(Dictionary<string, string> source,
            Pair to, IDictConverter.IDictConverter converter, RequestTranslation request, CancellationToken token)
        {
            var sourceText = converter.Serialize(source);
            var response = await request(sourceText, to, token);
            token.ThrowIfCancellationRequested();
            return ParseAiResponse(source, converter, response);
        }

        public static Dictionary<string, string> ParseAiResponse(Dictionary<string, string> source,
            IDictConverter.IDictConverter converter, string? response)
        {
            InvalidDataException? validationError = null;
            foreach (var candidate in ResponseCandidates(response, converter.Serialize(source).TrimStart().StartsWith('{')))
            {
                Dictionary<string, string>? values;
                try
                {
                    if (!converter.TryDeserialize(candidate, out values) || values == null) continue;
                }
                catch (Exception)
                {
                    // Third-party converters are not guaranteed to implement TryDeserialize without throwing.
                    // Treat that candidate as invalid and continue looking for a fenced/embedded payload.
                    continue;
                }

                try
                {
                    ProjectWorkspace.ValidateTranslation(source, values);
                    return values;
                }
                catch (InvalidDataException error)
                {
                    validationError ??= error;
                }
            }

            var detail = validationError == null
                ? "没有找到可解析的结构化译文"
                : validationError.Message;
            throw new InvalidDataException($"AI 返回内容无效：{detail}。响应开头：“{ResponsePreview(response)}”");
        }

        private static IEnumerable<string> ResponseCandidates(string? response, bool extractJsonObjects)
        {
            var text = (response ?? string.Empty).Trim();
            if (text.EndsWith("[end]", StringComparison.Ordinal)) text = text[..^5].TrimEnd();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (seen.Add(text)) yield return text;

            const string fence = "```";
            for (var start = text.IndexOf(fence, StringComparison.Ordinal);
                 start >= 0;
                 start = text.IndexOf(fence, start + fence.Length, StringComparison.Ordinal))
            {
                var contentStart = start + fence.Length;
                var end = text.IndexOf(fence, contentStart, StringComparison.Ordinal);
                if (end < 0) break;
                var block = text[contentStart..end];
                var lineEnd = block.IndexOf('\n');
                if (lineEnd >= 0 && Regex.IsMatch(block[..lineEnd].Trim(), @"^[A-Za-z0-9_.+-]{0,20}$"))
                    block = block[(lineEnd + 1)..];
                block = block.Trim();
                if (seen.Add(block)) yield return block;
                start = end;
            }

            if (!extractJsonObjects) yield break;
            foreach (var json in JsonObjects(text))
                if (seen.Add(json)) yield return json;
        }

        private static IEnumerable<string> JsonObjects(string text)
        {
            for (var start = 0; start < text.Length; start++)
            {
                if (text[start] != '{') continue;
                var depth = 0;
                var inString = false;
                var escaped = false;
                for (var index = start; index < text.Length; index++)
                {
                    var character = text[index];
                    if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (character == '\\') escaped = true;
                        else if (character == '"') inString = false;
                        continue;
                    }
                    if (character == '"') inString = true;
                    else if (character == '{') depth++;
                    else if (character == '}' && --depth == 0)
                    {
                        yield return text[start..(index + 1)];
                        start = index;
                        break;
                    }
                }
            }
        }

        private static string ResponsePreview(string? response)
        {
            var preview = Regex.Replace(response ?? string.Empty, @"\s+", " ").Trim();
            if (preview.Length == 0) return "<空响应>";
            return preview.Length <= 180 ? preview : preview[..180] + "…";
        }

        public static string FormatPrompt(string prompt, Pair to, string text)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !prompt.Contains("{1}"))
            {
                LogUtils.LogError("Prompt无效（需要包含待翻译内容占位符{1}），本次使用默认Prompt");
                prompt = ConfigUtils.DefaultAIPrompt;
            }
            try { return string.Format(CultureInfo.InvariantCulture, prompt, to.Name, text); }
            catch (FormatException)
            {
                LogUtils.LogError("Prompt格式有误（请检查花括号），本次使用默认Prompt");
                return string.Format(CultureInfo.InvariantCulture, DefaultPromptFormat, to.Name, text);
            }
        }
    }
}
