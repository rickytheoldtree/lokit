using System.IO;
using System.Text.RegularExpressions;
using Lokit.Data;
using Lokit.Projects;

namespace Lokit.Utils
{
    /// <summary>
    /// 一份“文件名-ISO”对应表，名字取自 iso 文件夹里的文件名
    /// </summary>
    public sealed record PairTable(string Name, List<Pair> Pairs)
    {
        public override string ToString() => Name;
    }

    public static class PairUtils
    {
        private static readonly List<Pair> FallBackList = new()
        {
            new Pair("English", "en"),
            new Pair("ChineseSimplified", "zh-CN")
        };

        //形如 en、zh-CN、pt-BR、zh-Hant 的语言标签
        private static readonly Regex LanguageTag = new(@"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$");

        private static IReadOnlyCollection<string> reportedProblems = Array.Empty<string>();

        public static IReadOnlyList<PairTable> Tables { get; private set; } = Load();

        /// <summary>
        /// 重新读取 iso 文件夹，改过语言表不用重启程序
        /// </summary>
        public static void Reload() => Tables = Load();

        public static PairTable? FindTable(string? name) =>
            Tables.FirstOrDefault(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 当前应使用的语言表：找不到同名表时用第一份，iso 文件夹里没有可用的表时为 null
        /// </summary>
        public static PairTable? ResolveTable(string? name) => FindTable(name) ?? (Tables.Count > 0 ? Tables[0] : null);

        public static List<Pair> GetPairs(string? name) => ResolveTable(name)?.Pairs ?? FallBackList;

        private static List<PairTable> Load()
        {
            var tables = LoadTables(PathUtils.IsoPath, out var problems);
            //下拉框每次展开都会重新读取，同样的问题只记录一次
            foreach (var problem in problems.Where(p => !reportedProblems.Contains(p))) LogUtils.LogError(problem);
            reportedProblems = problems;
            return tables;
        }

        /// <summary>
        /// 读取文件夹里的所有 .txt 语言表，按文件名排序。有问题的行跳过并记入 problems，没有可用语言的表不列出。
        /// </summary>
        public static List<PairTable> LoadTables(string folder, out List<string> problems)
        {
            problems = new List<string>();
            var tables = new List<PairTable>();
            if (!Directory.Exists(folder)) return tables;

            var files = Directory.GetFiles(folder)
                .Where(file => string.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                try
                {
                    var pairs = Parse(File.ReadAllLines(file), fileName, problems);
                    if (pairs.Count == 0)
                    {
                        problems.Add($"语言表 {fileName} 中没有可用的语言，已忽略。");
                        continue;
                    }
                    tables.Add(new PairTable(Path.GetFileNameWithoutExtension(file), pairs));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    problems.Add($"读取语言表 {fileName} 失败：{e.Message}");
                }
            }
            return tables;
        }

        /// <summary>
        /// 每行“文件名,ISO代码”，空行和 # 开头的注释行跳过
        /// </summary>
        private static List<Pair> Parse(IEnumerable<string> lines, string fileName, List<string> problems)
        {
            var pairs = new List<Pair>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lineNumber = 0;
            foreach (var rawLine in lines)
            {
                lineNumber++;
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var where = $"语言表 {fileName} 第 {lineNumber} 行";
                var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 2)
                {
                    problems.Add($"{where}已忽略：格式应为“文件名,ISO代码”。");
                    continue;
                }

                var (name, code) = (parts[0], parts[1]);
                if (!ProjectWorkspace.IsUsableLanguageName(name) || ProjectWorkspace.IsReservedLanguageName(name))
                    problems.Add($"{where}已忽略：“{name}”不能用作文件名。");
                else if (!names.Add(name))
                    problems.Add($"{where}已忽略：文件名 {name} 重复。");
                else
                {
                    //代码写错只影响机器翻译，语言本身仍可编辑和导出
                    if (!LanguageTag.IsMatch(code))
                        problems.Add($"{where}：“{code}”不像 ISO 语言代码，机器翻译可能无法识别。");
                    pairs.Add(new Pair(name, code));
                }
            }
            return pairs;
        }

        /// <summary>
        /// 旧版本按序号记住语言表，序号对应当时 iso 文件夹里所有文件的枚举顺序
        /// </summary>
        public static string? LegacyTableName(string folder, int index)
        {
            if (index < 0 || !Directory.Exists(folder)) return null;
            var names = Directory.GetFiles(folder).Select(Path.GetFileNameWithoutExtension).Distinct().ToList();
            return index < names.Count ? names[index] : null;
        }
    }
}
