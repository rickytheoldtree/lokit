using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lokit.Data;
using Lokit.DictConverter;
using Lokit.Utils;
using Newtonsoft.Json;

namespace Lokit.Projects
{
    public sealed partial class ProjectWorkspace
    {
        private readonly object sync = new();
        private ProjectDocument document;
        private string fileHash;
        private long revision;
        public string FilePath { get; }
        public Type ConverterType { get; }
        public event Action? Changed;

        private ProjectWorkspace(string path, ProjectDocument data, Type converterType, string hash)
        {
            FilePath = Path.GetFullPath(path);
            document = data;
            ConverterType = converterType;
            fileHash = hash;
        }

        public string Name { get { lock (sync) return document.Name; } }
        public string TargetDirectory { get { lock (sync) return document.TargetDirectory; } }
        public long Revision { get { lock (sync) return revision; } }
        public string BackupDirectory => FilePath + ".backups";
        public bool HasBackup { get { lock (sync) return document.LastBackup != null; } }
        public bool HasPendingMerge { get { lock (sync) return document.PendingMerge != null; } }
        public bool HasMergeBackup { get { lock (sync) return document.LastMergeBackup != null; } }
        public IReadOnlyList<Pair> Pairs { get { lock (sync) return document.Languages.Select(l => new Pair(l.Name, l.IsoCode)).ToArray(); } }
        public Pair SourceLanguage { get { lock (sync) { var l = Language(document, document.SourceLanguage); return new Pair(l.Name, l.IsoCode); } } }
        public int PendingChangeCount { get { lock (sync) return document.Languages.Sum(l => Diff(l).Count); } }
        public int PendingRetranslationCount { get { lock (sync) return document.Languages.Sum(l => l.RetranslateKeys.Count); } }

        public static ProjectWorkspace Create(string path, string targetDirectory, IEnumerable<Pair> pairs,
            string sourceLanguage, Type converterType)
        {
            path = Path.GetFullPath(path);
            if (File.Exists(path)) throw new IOException("项目文件已经存在，请选择新文件，或使用“打开项目”。");
            var data = new ProjectDocument
            {
                Name = Path.GetFileNameWithoutExtension(path),
                TargetDirectory = Path.GetFullPath(targetDirectory),
                ConverterType = converterType.FullName ?? throw new InvalidOperationException("无效的转换器"),
                SourceLanguage = sourceLanguage,
                Languages = pairs.Select(p => new ProjectLanguage { Name = p.Name, IsoCode = p.isoCode }).ToList(),
            };
            Validate(data);
            if (!Directory.Exists(data.TargetDirectory)) throw new DirectoryNotFoundException("语言目录不存在。");
            var converter = CreateConverter(converterType);
            foreach (var language in data.Languages)
            {
                var filename = TargetPath(data, language.Name);
                if (!File.Exists(filename)) continue;
                var bytes = File.ReadAllBytes(filename);
                language.Working = Parse(bytes, converter, filename);
                language.Baseline = Copy(language.Working);
                language.BaselineHash = Hash(bytes);
            }
            var projectBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data, Formatting.Indented));
            // Publish the complete project atomically without overwriting a concurrently created file.
            IOUtils.AtomicWriteAllBytes(path, projectBytes, overwrite: false);
            var workspace = new ProjectWorkspace(path, data, converterType, Hash(projectBytes));
            lock (workspace.sync) workspace.RecordVersions("创建项目", data.Languages.Select(l => new LanguageSnapshot { Name = l.Name, Values = Copy(l.Baseline) }));
            return workspace;
        }

        public static ProjectWorkspace Open(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var data = JsonConvert.DeserializeObject<ProjectDocument>(Decode(bytes))
                ?? throw new InvalidDataException("项目文件为空。");
            // Version 1 projects created before forced retranslation did not contain this field.
            foreach (var language in data.Languages ?? new())
                if (language != null) language.RetranslateKeys ??= new(StringComparer.Ordinal);
            Validate(data);
            var converterType = DictConverterUtils.Converters.Select(c => c.type)
                .FirstOrDefault(t => t?.FullName == data.ConverterType)
                // 改名为 Lokit 之前创建的项目里，内置转换器的命名空间还是旧名字
                ?? DictConverterUtils.Converters.Select(c => c.type).FirstOrDefault(t =>
                    t?.FullName == ConfigUtils.RenameLegacyNamespace(data.ConverterType))
                ?? throw new InvalidDataException($"缺少转换器 {data.ConverterType}，请先安装对应 DLL。");
            return new ProjectWorkspace(path, data, converterType, Hash(bytes));
        }

        public Dictionary<string, string> ReadLanguage(string name)
        {
            lock (sync) return Copy(Language(document, name).Working);
        }

        public bool ContainsSourceKey(string key)
        {
            lock (sync) return Language(document, document.SourceLanguage).Working.ContainsKey(key);
        }

        public void AddSourceEntries(Dictionary<string, string> entries)
        {
            Mutate(next =>
            {
                var source = Language(next, next.SourceLanguage);
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key)) throw new InvalidDataException("Key 不能为空。");
                    if (!source.Working.TryAdd(entry.Key, entry.Value))
                        throw new InvalidDataException($"Key“{entry.Key}”已存在，请在编辑页修改。");
                }
            });
        }

        public void SaveLanguage(string name, Dictionary<string, string> values, Dictionary<string, string> expected)
        {
            Mutate(next =>
            {
                var language = Language(next, name);
                var merged = Copy(language.Working);
                foreach (var key in expected.Keys.Union(values.Keys, StringComparer.Ordinal))
                {
                    expected.TryGetValue(key, out var before);
                    values.TryGetValue(key, out var desired);
                    language.Working.TryGetValue(key, out var current);
                    if (before == desired) continue;
                    if (current != before && current != desired)
                    {
                        EnsureNoPendingMerge();
                        throw new EditorMergeConflictException(BuildMergePlan(new MergeRequest
                        {
                            Title = "合并我的编辑",
                            Basis = new() { new LanguageSnapshot { Name = name, Values = Copy(expected) } },
                            Incoming = new() { new LanguageSnapshot { Name = name, Values = Copy(values) } },
                        }));
                    }
                    if (desired == null) merged.Remove(key);
                    else merged[key] = desired;
                }
                language.Working = merged;
            });
        }

        public void DeleteKeys(IEnumerable<string> keys)
        {
            var selected = keys.Distinct(StringComparer.Ordinal).ToArray();
            Mutate(next =>
            {
                foreach (var language in next.Languages)
                    foreach (var key in selected)
                    {
                        language.Working.Remove(key);
                        language.RetranslateKeys.Remove(key);
                    }
            });
        }

        public void SetRetranslation(IEnumerable<string> keys, IEnumerable<string> targets, bool pending)
        {
            var selectedKeys = keys.Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal).ToArray();
            var selectedTargets = targets.Where(target => !string.IsNullOrWhiteSpace(target))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (selectedKeys.Length == 0) throw new InvalidOperationException("请至少选择一个 Key。");
            if (selectedTargets.Length == 0) throw new InvalidOperationException("请至少选择一种目标语言。");
            Mutate(next =>
            {
                var source = Language(next, next.SourceLanguage);
                foreach (var key in selectedKeys)
                    if (!source.Working.ContainsKey(key)) throw new InvalidDataException($"源语言中不存在 Key“{key}”。");
                foreach (var name in selectedTargets)
                {
                    if (name == next.SourceLanguage) throw new InvalidOperationException("源语言不能标记为待重译。");
                    var language = Language(next, name);
                    foreach (var key in selectedKeys)
                    {
                        if (pending) language.RetranslateKeys.Add(key);
                        else language.RetranslateKeys.Remove(key);
                    }
                }
            });
        }

        public TranslationInput PrepareTranslation(string target, Dictionary<string, string> sourceSnapshot)
        {
            lock (sync)
            {
                var language = Language(document, target);
                if (target == document.SourceLanguage) throw new InvalidOperationException("源语言不能作为翻译目标。");
                var entries = sourceSnapshot.Where(p => language.RetranslateKeys.Contains(p.Key) ||
                    !language.Working.TryGetValue(p.Key, out var value) ||
                    string.IsNullOrWhiteSpace(value))
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                return new TranslationInput(document.Id, target, entries, Copy(language.Working),
                    language.RetranslateKeys.ToHashSet(StringComparer.Ordinal));
            }
        }

        public void ApplyTranslation(TranslationInput input, Dictionary<string, string> translated)
        {
            ValidateTranslation(input.Source, translated);
            Mutate(next =>
            {
                if (input.ProjectId != next.Id) throw new InvalidOperationException("翻译结果不属于当前项目。");
                var source = Language(next, next.SourceLanguage).Working;
                var target = Language(next, input.TargetLanguage);
                foreach (var entry in input.Source)
                {
                    if (!source.TryGetValue(entry.Key, out var current) || current != entry.Value)
                        throw new InvalidOperationException($"源文案“{entry.Key}”已变化，未应用旧译文。");
                    var existed = input.TargetBefore.TryGetValue(entry.Key, out var before);
                    if (target.Working.TryGetValue(entry.Key, out var now) != existed || now != before)
                        throw new InvalidOperationException($"译文“{entry.Key}”已被修改，未覆盖当前内容。");
                    target.Working[entry.Key] = translated[entry.Key];
                    if (input.RetranslateBefore.Contains(entry.Key)) target.RetranslateKeys.Remove(entry.Key);
                }
            });
        }

        public PublishPlan PreviewPublish()
        {
            lock (sync)
            {
                var conflicts = new List<string>();
                var files = new List<string>();
                var changes = new List<EntryChange>();
                if (document.PendingMerge != null) conflicts.Add("还有未完成的合并，请先继续处理或放弃本次合并。");
                if (CurrentHash(FilePath) != fileHash) conflicts.Add("项目文件已被外部修改，请重新打开项目。");
                foreach (var language in document.Languages)
                {
                    var diff = Diff(language);
                    changes.AddRange(diff);
                    // Do not create empty files for languages that have never been translated.
                    if (diff.Count == 0) continue;
                    var path = TargetPath(document, language.Name);
                    files.Add(language.Name);
                    if (CurrentHash(path) != language.BaselineHash)
                        conflicts.Add($"{language.Name}.json 已在外部修改或删除；未覆盖，请先处理外部变化。");
                }
                return new PublishPlan(document.Id, fileHash, revision, changes, conflicts, files);
            }
        }

        public string Publish(PublishPlan plan)
        {
            string backupPath;
            lock (sync)
            {
                using var lease = AcquireProjectLock();
                using var targetLease = AcquireTargetLock();
                if (plan.ProjectId != document.Id || plan.StateHash != fileHash || plan.Revision != revision)
                    throw new InvalidOperationException("项目或工作数据已变化，请重新预览发布。");
                var current = PreviewPublish();
                if (!current.CanPublish) throw new InvalidOperationException(current.Conflicts.Count > 0
                    ? string.Join(Environment.NewLine, current.Conflicts) : "没有待发布的更改。");
                var converter = CreateConverter(ConverterType);
                var outputs = current.Files.ToDictionary(name => name,
                    name => Encoding.UTF8.GetBytes(converter.Serialize(Language(document, name).Working)));
                var backup = new PublishBackup { ProjectId = document.Id, TargetDirectory = document.TargetDirectory };
                foreach (var name in current.Files)
                {
                    var path = TargetPath(document, name);
                    var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
                    if ((before == null ? null : Hash(before)) != Language(document, name).BaselineHash)
                        throw new IOException("目标文件已变化，请重新预览发布。");
                    backup.Files.Add(new BackupFile { Language = name, Before = before, PublishedHash = Hash(outputs[name]) });
                }
                var backupName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json";
                backupPath = Path.Combine(BackupDirectory, backupName);
                IOUtils.AtomicWriteAllText(backupPath, JsonConvert.SerializeObject(backup, Formatting.Indented));
                var written = new List<BackupFile>();
                try
                {
                    foreach (var item in backup.Files)
                    {
                        var path = TargetPath(document, item.Language);
                        if (CurrentHash(path) != (item.Before == null ? null : Hash(item.Before)))
                            throw new IOException($"{item.Language} 在发布过程中被外部修改。");
                        IOUtils.AtomicWriteAllBytes(path, outputs[item.Language]);
                        written.Add(item);
                    }
                    var next = Clone(document);
                    foreach (var item in backup.Files)
                    {
                        var language = Language(next, item.Language);
                        language.Baseline = Copy(language.Working);
                        language.BaselineHash = item.PublishedHash;
                    }
                    next.LastBackup = backupName;
                    Commit(next);
                }
                catch (Exception error)
                {
                    var failures = Rollback(written, document);
                    throw new IOException($"发布失败。{failures}备份：{backupPath}", error);
                }
                RecordVersions("导出", current.Files.Select(name => new LanguageSnapshot { Name = name, Values = Copy(Language(document, name).Working) }));
            }
            Changed?.Invoke();
            return backupPath;
        }

        public void RestoreLastPublish()
        {
            lock (sync)
            {
                using var lease = AcquireProjectLock();
                using var targetLease = AcquireTargetLock();
                if (document.LastBackup == null) throw new InvalidOperationException("没有可恢复的发布备份。");
                if (CurrentHash(FilePath) != fileHash) throw new IOException("项目文件已被外部修改，请重新打开。");
                var backup = JsonConvert.DeserializeObject<PublishBackup>(File.ReadAllText(Path.Combine(BackupDirectory, document.LastBackup)))
                    ?? throw new InvalidDataException("备份文件无效。");
                if (backup.ProjectId != document.Id || backup.TargetDirectory != document.TargetDirectory ||
                    backup.Files.Select(f => f.Language).Distinct(StringComparer.Ordinal).Count() != backup.Files.Count)
                    throw new InvalidDataException("备份不属于当前项目。");
                var reverse = new List<BackupFile>();
                foreach (var item in backup.Files)
                {
                    var path = TargetPath(document, item.Language);
                    if (CurrentHash(path) != item.PublishedHash) throw new IOException($"{item.Language} 发布后已被外部修改，不能直接恢复。");
                    reverse.Add(new BackupFile
                    {
                        Language = item.Language,
                        Before = File.ReadAllBytes(path),
                        PublishedHash = item.Before == null ? null : Hash(item.Before)
                    });
                }
                var next = Clone(document);
                var converter = CreateConverter(ConverterType);
                foreach (var item in backup.Files)
                {
                    var language = Language(next, item.Language);
                    language.Baseline = item.Before == null ? new() : Parse(item.Before, converter, item.Language);
                    language.BaselineHash = item.Before == null ? null : Hash(item.Before);
                }
                var written = new List<BackupFile>();
                try
                {
                    for (var i = 0; i < backup.Files.Count; i++)
                    {
                        var item = backup.Files[i];
                        var path = TargetPath(document, item.Language);
                        if (CurrentHash(path) != item.PublishedHash) throw new IOException("恢复过程中目标文件发生变化。");
                        RestoreFile(path, item.Before);
                        written.Add(reverse[i]);
                    }
                    next.LastBackup = null;
                    Commit(next);
                }
                catch (Exception error)
                {
                    throw new IOException("恢复失败。" + Rollback(written, document), error);
                }
            }
            Changed?.Invoke();
        }

        public static void ValidateTranslation(Dictionary<string, string> source, Dictionary<string, string> result)
        {
            if (!source.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(result.Keys))
                throw new InvalidDataException("译文 Key 与本次请求不一致，未应用结果。");
            foreach (var item in source)
            {
                if (!string.IsNullOrWhiteSpace(item.Value) && string.IsNullOrWhiteSpace(result[item.Key]))
                    throw new InvalidDataException($"“{item.Key}”返回了空译文。");
                if (!Placeholders(item.Value).SequenceEqual(Placeholders(result[item.Key])))
                    throw new InvalidDataException($"“{item.Key}”的占位符不一致，未应用结果。");
            }
        }

        private static string[] Placeholders(string text) => Regex.Matches(text ?? string.Empty,
            @"\{[^{}\r\n]+\}|%(?:\d+\$)?[sdif]|<[^<>\r\n]+>").Select(m => m.Value).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        private void Mutate(Action<ProjectDocument> mutation)
        {
            lock (sync)
            {
                using var lease = AcquireProjectLock();
                RefreshWorkspaceFromDisk();
                var next = Clone(document);
                mutation(next);
                Commit(next);
            }
            Changed?.Invoke();
        }

        private FileStream AcquireProjectLock() => new(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        private void RefreshWorkspaceFromDisk()
        {
            if (CurrentHash(FilePath) == fileHash) return;
            var latest = Open(FilePath);
            if (latest.document.Id != document.Id || latest.document.TargetDirectory != document.TargetDirectory ||
                latest.document.SourceLanguage != document.SourceLanguage || latest.document.ConverterType != document.ConverterType ||
                !latest.Pairs.SequenceEqual(Pairs))
                throw new IOException("磁盘上的项目配置已变化，请另存当前编辑内容后重新打开项目。");
            document = latest.document;
            fileHash = latest.fileHash;
            revision++;
        }

        private FileStream AcquireTargetLock()
        {
            return new FileStream(Path.Combine(document.TargetDirectory, ".localization-publish.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        private void Commit(ProjectDocument next)
        {
            if (CurrentHash(FilePath) != fileHash) throw new IOException("项目文件已被其他程序修改，请重新打开；当前内容未覆盖磁盘文件。");
            Validate(next);
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(next, Formatting.Indented));
            IOUtils.AtomicWriteAllBytes(FilePath, bytes);
            fileHash = Hash(bytes);
            document = next;
            revision++;
        }

        private static List<EntryChange> Diff(ProjectLanguage language)
        {
            var changes = new List<EntryChange>();
            foreach (var pair in language.Working)
            {
                if (!language.Baseline.TryGetValue(pair.Key, out var before))
                    changes.Add(new(language.Name, pair.Key, "新增", null, pair.Value));
                else if (before != pair.Value) changes.Add(new(language.Name, pair.Key, "修改", before, pair.Value));
            }
            foreach (var pair in language.Baseline)
                if (!language.Working.ContainsKey(pair.Key)) changes.Add(new(language.Name, pair.Key, "删除", pair.Value, null));
            return changes;
        }

        /// <summary>
        /// 语言名直接用作正式目录里的 JSON 文件名
        /// </summary>
        internal static bool IsUsableLanguageName(string? name) =>
            !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            name is not ("." or "..") && !name.EndsWith('.') && !name.EndsWith(' ');

        internal static bool IsReservedLanguageName(string name) =>
            Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase);

        private static void Validate(ProjectDocument data)
        {
            if (data.Version != 1 || string.IsNullOrWhiteSpace(data.Id) || string.IsNullOrWhiteSpace(data.ConverterType) ||
                data.Languages == null || data.Languages.Count == 0 ||
                !Path.IsPathFullyQualified(data.TargetDirectory))
                throw new InvalidDataException("项目格式或目标目录无效。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var language in data.Languages)
            {
                if (language == null || !IsUsableLanguageName(language.Name) ||
                    !names.Add(language.Name) || string.IsNullOrWhiteSpace(language.IsoCode) ||
                    language.Working == null || language.Baseline == null || language.RetranslateKeys == null)
                    throw new InvalidDataException("语言表包含无效或重复的文件名/语言配置。");
                if (IsReservedLanguageName(language.Name))
                    throw new InvalidDataException("语言文件名不能使用 Windows 保留名称。");
                if (language.Working.Values.Any(v => v == null) || language.Baseline.Values.Any(v => v == null))
                    throw new InvalidDataException("项目文本不能为 null。");
            }
            var source = Language(data, data.SourceLanguage);
            if (source.RetranslateKeys.Count > 0 || data.Languages.Any(language =>
                language.RetranslateKeys.Any(key => !source.Working.ContainsKey(key))))
                throw new InvalidDataException("待重译标记包含无效的源语言或 Key。");
            if (data.LastBackup != null && (Path.GetFileName(data.LastBackup) != data.LastBackup ||
                data.LastBackup.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw new InvalidDataException("备份路径无效。");
            ValidateMerge(data);
        }

        private static ProjectLanguage Language(ProjectDocument data, string name) => data.Languages.FirstOrDefault(l => l.Name == name)
            ?? throw new InvalidDataException($"语言“{name}”不属于当前项目。");
        private static string TargetPath(ProjectDocument data, string name)
        {
            _ = Language(data, name);
            return Path.Combine(data.TargetDirectory, name + ".json");
        }
        private static Dictionary<string, string> Copy(Dictionary<string, string> source) => new(source, StringComparer.Ordinal);
        private static bool Same(Dictionary<string, string> left, Dictionary<string, string> right) => left.Count == right.Count &&
            left.All(p => right.TryGetValue(p.Key, out var value) && value == p.Value);
        private static ProjectDocument Clone(ProjectDocument data) => JsonConvert.DeserializeObject<ProjectDocument>(JsonConvert.SerializeObject(data))!;
        private static IDictConverter.IDictConverter CreateConverter(Type type) => DictConverterUtils.GetConverterInstance(type)
            ?? throw new InvalidOperationException("无法创建项目转换器。");
        private static Dictionary<string, string> Parse(byte[] bytes, IDictConverter.IDictConverter converter, string path)
        {
            try
            {
                if (converter.TryDeserialize(Decode(bytes), out var data) && data != null)
                    return data.ToDictionary(p => p.Key, p => p.Value ?? string.Empty, StringComparer.Ordinal);
            }
            catch (Exception e) { throw new InvalidDataException($"无法解析 {path}：{e.Message}", e); }
            throw new InvalidDataException($"无法解析 {path}");
        }
        private static string Decode(byte[] bytes)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
        private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        private static string? CurrentHash(string path) => File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null;
        private static void RestoreFile(string path, byte[]? bytes)
        {
            if (bytes == null) File.Delete(path);
            else IOUtils.AtomicWriteAllBytes(path, bytes);
        }
        private static string Rollback(IEnumerable<BackupFile> written, ProjectDocument data)
        {
            var errors = new List<string>();
            foreach (var item in written.Reverse())
                try
                {
                    var path = TargetPath(data, item.Language);
                    if (CurrentHash(path) != item.PublishedHash) throw new IOException("文件已被外部修改，跳过自动恢复以免覆盖。");
                    RestoreFile(path, item.Before);
                }
                catch (Exception e) { errors.Add($"{item.Language}: {e.Message}"); }
            return errors.Count == 0 ? "已恢复本次写入的文件。" : "部分文件恢复失败：" + string.Join("；", errors);
        }
    }
}
