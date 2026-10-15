using System.Collections.ObjectModel;
using System.Text;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.DictConverter;
using LocalizationTranslationWPF.Utils;
using OpenQA.Selenium;

namespace LocalizationTranslationWPF.Translate
{
    public class DeepLWebDriverTranslator : WebDriverTranslator
    {
        private const string Url = "https://www.deepl.com/en/translator";

        /// <summary>
        /// 未登录时 DeepL 网页单次只翻译前 1500 个字符，超出部分会被静默丢弃
        /// </summary>
        private const int MaxRequestLength = 1500;

        private const string Separator = "-----------------------";

        private const int PollIntervalMs = 100;

        /// <summary>等待单批译文完成的最长时间（60秒）。</summary>
        private const int ReplyWaitTryCount = 600;

        /// <summary>译文连续保持不变的轮询次数，DeepL 会先给出初稿再逐步修正。</summary>
        private const int StableTryCount = 10;

        /// <summary>
        /// 一次性读取页面状态。每个段落是一个 p 元素，按段落拼接才能还原原文中的单个换行
        /// （innerText 会在段落之间输出空行）。
        /// </summary>
        private const string ReadStateScript = """
            const read = testId => {
                const box = document.querySelector(`d-textarea[data-testid='${testId}']`);
                if (!box) return null;
                const paragraphs = box.querySelectorAll('p');
                return paragraphs.length > 0
                    ? Array.from(paragraphs, p => p.textContent).join('\n')
                    : box.innerText;
            };
            return [location.hash, read('translator-source-input'), read('translator-target-input')];
            """;

        private DeepLWebDriverTranslator(Config.WebDriverType type, string driverPath) : base(type, driverPath)
        {
        }

        public static async Task<bool> Run(CancellationToken token)
        {
            return await Task.Run(async () =>
            {
                var dictConverter = DictConverterUtils.GetConverterInstance(ConfigUtils.DictConverter.Value);
                if (dictConverter == null)
                {
                    throw new InvalidOperationException("字典转换器类型错误");
                }

                var driver = await WebDriverProvider.PrepareAsync(ConfigUtils.WebDriver.Value, token);
                using var translator = new DeepLWebDriverTranslator(driver.Type, driver.DriverPath);
                var random = new Random();
                return await TranslationJob.RunAsync(dictConverter,
                    (from, to, source, converter, t) => translator.TranslateAsync(from, to, source, random, t),
                    () => RandomDelay(random, token), token);
            }, token);
        }

        private async Task<Dictionary<string, string>?> TranslateAsync(Pair from, Pair to,
            Dictionary<string, string> originJson, Random random, CancellationToken token)
        {
            var sourceCode = ToSourceCode(from.isoCode);
            var targetCode = ToTargetCode(to.isoCode);
            var result = new Dictionary<string, string>();
            var batchKeys = new List<string>();
            var batchValues = new List<string>();
            var batchLength = 0;
            var isFirstBatch = true;

            async Task<bool> Flush()
            {
                if (batchKeys.Count == 0) return true;
                if (!isFirstBatch) await RandomDelay(random, token);
                isFirstBatch = false;

                var values = await Translate(batchValues, sourceCode, targetCode, token);
                if (values == null) return false;
                for (var i = 0; i < batchKeys.Count; i++)
                {
                    result[batchKeys[i]] = values[i];
                }

                batchKeys.Clear();
                batchValues.Clear();
                batchLength = 0;
                return true;
            }

            foreach (var d in originJson)
            {
                var value = d.Value.Replace("\r\n", "\n").Trim();
                //空文本没必要发给DeepL，而且空段落会被页面吞掉，导致分隔数量对不上
                if (value.Length == 0)
                {
                    result[d.Key] = d.Value;
                    continue;
                }

                var itemLength = value.Length + Separator.Length + 2;
                if (itemLength > MaxRequestLength)
                {
                    LogUtils.LogError($"单个文本长度超过{MaxRequestLength}，无法翻译：{d.Key}；请自行切分后重新尝试");
                    return null;
                }

                //超长时先把已攒下的一批发出去
                if (batchLength + itemLength > MaxRequestLength && !await Flush())
                {
                    return null;
                }

                batchKeys.Add(d.Key);
                batchValues.Add(value);
                batchLength += itemLength;
            }

            return await Flush() ? result : null;
        }

        private async Task<List<string>?> Translate(List<string> values, string from, string to,
            CancellationToken token)
        {
            var text = string.Join($"\n{Separator}\n", values);
            //只改 hash 时页面不会重新翻译，先离开再进入，保证每批都是一次完整加载
            if (!ToUrl("about:blank")) return null;
            if (!ToUrl($"{Url}#{from}/{to}/{GetHashSafeString(text)}")) return null;

            var expectedSource = RemoveWhiteSpace(text);
            string? lastTarget = null;
            var stableCount = 0;
            var hashConsumed = false;
            var sourceMatched = false;
            List<string>? lastSegments = null;
            for (var count = 0; count < ReplyWaitTryCount; count++)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(PollIntervalMs, token);

                if (!TryReadState(out var hash, out var source, out var target)) continue;

                //页面不认识语言代码时不会消费 hash，并且会继续显示上一次的内容
                hashConsumed = hash.Length <= 1;
                sourceMatched = RemoveWhiteSpace(source) == expectedSource;
                if (!hashConsumed || !sourceMatched || string.IsNullOrWhiteSpace(target))
                {
                    stableCount = 0;
                    lastTarget = null;
                    continue;
                }

                if (target != lastTarget)
                {
                    lastTarget = target;
                    stableCount = 0;
                    continue;
                }

                if (++stableCount < StableTryCount) continue;

                lastSegments = SplitSegments(target);
                if (lastSegments.Count == values.Count && lastSegments.All(s => s.Length > 0))
                {
                    return lastSegments;
                }
            }

            if (!hashConsumed)
            {
                LogUtils.LogError($"DeepL无法识别语言代码 {from} → {to}，请检查语言规则中的ISO代码");
            }
            else if (!sourceMatched)
            {
                LogUtils.LogError("DeepL原文框内容与发送的不一致，可能超出了单次字数限制或页面已更新");
            }
            else if (lastSegments != null)
            {
                LogUtils.LogError($"DeepL翻译后数量与原文本数量不一致（{lastSegments.Count}/{values.Count}）");
            }
            else
            {
                LogUtils.LogError("等待DeepL翻译结果超时");
            }

            return null;
        }

        private bool TryReadState(out string hash, out string source, out string target)
        {
            hash = source = target = "";
            try
            {
                if (WebDriver is not IJavaScriptExecutor js) return false;
                if (js.ExecuteScript(ReadStateScript) is not ReadOnlyCollection<object> state || state.Count != 3)
                {
                    return false;
                }

                hash = state[0]?.ToString() ?? "";
                source = state[1]?.ToString() ?? "";
                target = state[2]?.ToString() ?? "";
                return true;
            }
            catch (WebDriverException)
            {
                // 页面加载或重新渲染期间读取失败，下一轮再试。
                return false;
            }
        }

        private static List<string> SplitSegments(string target)
        {
            var segments = new List<string>();
            var current = new StringBuilder();
            foreach (var line in target.Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length >= 3 && trimmed.All(c => c == '-'))
                {
                    segments.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                if (current.Length > 0) current.Append('\n');
                current.Append(line);
            }

            segments.Add(current.ToString().Trim());
            return segments;
        }

        /// <summary>
        /// DeepL 源语言只接受基础代码，例如 zh、pt、en
        /// </summary>
        private static string ToSourceCode(string isoCode)
        {
            var code = isoCode.Trim().ToLowerInvariant();
            var dash = code.IndexOf('-');
            return dash > 0 ? code[..dash] : code;
        }

        /// <summary>
        /// DeepL 目标语言要区分简繁中文，其余沿用语言规则里的代码（如 en-gb、pt-br）
        /// </summary>
        private static string ToTargetCode(string isoCode)
        {
            var code = isoCode.Trim().ToLowerInvariant();
            return code switch
            {
                "zh" or "zh-cn" or "zh-sg" or "zh-hans" => "zh-hans",
                "zh-tw" or "zh-hk" or "zh-mo" or "zh-hant" => "zh-hant",
                _ => code,
            };
        }

        /// <summary>
        /// hash 里的 / 和 | 是语言与原文/译文的分隔符，需要用反斜杠转义后再整体 URL 编码
        /// </summary>
        private static string GetHashSafeString(string str)
        {
            str = str.Replace("\\", "\\\\");
            str = str.Replace("/", "\\/");
            str = str.Replace("|", "\\|");
            return Uri.EscapeDataString(str);
        }

        private static string RemoveWhiteSpace(string str)
        {
            return string.Concat(str.Where(c => !char.IsWhiteSpace(c)));
        }
    }
}
