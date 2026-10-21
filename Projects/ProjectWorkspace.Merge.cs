using System.IO;
using System.Text;
using Lokit.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lokit.Projects
{
    public sealed partial class ProjectWorkspace
    {
        private const int MaxVersionsPerLanguage = 30;

        private string HistoryDirectory => FilePath + ".history";
        private string VersionDirectory(string language) => Path.Combine(HistoryDirectory, "versions", language);

        public MergePlan PreviewLanguageImport(IEnumerable<KeyValuePair<string, string>> languageFiles)
        {
            lock (sync)
            {
                RefreshWorkspaceFromDisk();
                EnsureNoPendingMerge();
                var request = new MergeRequest { Basis = new(), HistoryKind = "收到" };
                var names = new HashSet<string>(StringComparer.Ordinal);
                var fileNames = new List<string>();
                var converter = CreateConverter(ConverterType);
                foreach (var file in languageFiles)
                {
                    var language = Language(document, file.Key);
                    if (!names.Add(file.Key)) throw new InvalidDataException($"一次只能导入一个“{file.Key}”文件，请分别接收这些修订。");
                    var bytes = File.ReadAllBytes(file.Value);
                    // Reject duplicate properties before a converter can silently discard a value.
                    JToken.Parse(Decode(bytes), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    var values = Parse(bytes, converter, file.Value);
                    if (values.Keys.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("导入文件的 Key 不能为空。");
                    request.Incoming.Add(new LanguageSnapshot { Name = file.Key, Values = values });
                    fileNames.Add(Path.GetFileName(file.Value));
                    var match = MatchBase(values, Candidates(language));
                    if (match == null)
                    {
                        request.Notes.Add($"{file.Key}：没有找到这份文件对应的导出版本，内容不同的已有文案需要逐条确认；文件中缺少的 Key 不会删除。");
                        continue;
                    }
                    var (version, partial) = match.Value;
                    // A file containing only some keys says nothing about the others, so never infer deletions from it.
                    var basis = partial ? version.Values.Where(p => values.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
                        : Copy(version.Values);
                    request.Basis.Add(new LanguageSnapshot { Name = file.Key, Values = basis });
                    request.Notes.Add($"{file.Key}：以{Describe(version)}为基准" + (partial ? "。文件只包含部分 Key，缺少的 Key 保持不变。" : "。"));
                }
                if (request.Incoming.Count == 0) throw new InvalidDataException("没有可导入的语言文件。");
                request.Title = fileNames.Count == 1 ? fileNames[0] : $"{fileNames.Count} 个文案文件";
                return BuildMergePlan(request);
            }
        }

        public MergePlan PreviewTargetSync()
        {
            lock (sync)
            {
                RefreshWorkspaceFromDisk();
                EnsureNoPendingMerge();
                var converter = CreateConverter(ConverterType);
                var request = new MergeRequest { Title = "同步游戏资源", Basis = new(), HistoryKind = "同步", TargetHashes = new(StringComparer.Ordinal) };
                foreach (var language in document.Languages)
                {
                    var path = TargetPath(document, language.Name);
                    var bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
                    request.Basis.Add(new LanguageSnapshot { Name = language.Name, Values = Copy(language.Baseline) });
                    request.Incoming.Add(new LanguageSnapshot { Name = language.Name, Values = bytes == null ? new(StringComparer.Ordinal) : Parse(bytes, converter, path) });
                    request.TargetHashes.Add(language.Name, bytes == null ? null : Hash(bytes));
                }
                return BuildMergePlan(request);
            }
        }

        public MergePlan PreviewPendingMerge()
        {
            lock (sync)
            {
                RefreshWorkspaceFromDisk();
                return BuildMergePlan(CloneRequest(document.PendingMerge ?? throw new InvalidOperationException("没有待处理的合并。")));
            }
        }

        public MergePlan RefreshMergePlan(MergePlan plan)
        {
            lock (sync)
            {
                RefreshWorkspaceFromDisk();
                if (plan.ProjectId != document.Id) throw new InvalidOperationException("合并预览不属于当前项目。");
                var request = CloneRequest(plan.Request);
                request.Choices = plan.Entries.ToList();
                if (request.TargetHashes != null)
                {
                    var converter = CreateConverter(ConverterType);
                    foreach (var incoming in request.Incoming)
                    {
                        var path = TargetPath(document, incoming.Name);
                        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
                        incoming.Values = bytes == null ? new(StringComparer.Ordinal) : Parse(bytes, converter, path);
                        request.TargetHashes[incoming.Name] = bytes == null ? null : Hash(bytes);
                    }
                }
                return BuildMergePlan(request);
            }
        }

        public void SaveMergeDraft(MergePlan plan)
        {
            Mutate(next =>
            {
                CheckMergePlan(plan);
                var request = CloneRequest(plan.Request);
                request.Choices = plan.Entries.ToList();
                next.PendingMerge = CloneRequest(request);
            });
        }

        public void DiscardMergeDraft() => Mutate(next => next.PendingMerge = null);

        public void ApplyMerge(MergePlan plan)
        {
            Mutate(next =>
            {
                CheckMergePlan(plan);
                if (plan.UnresolvedCount > 0) throw new InvalidOperationException($"还有 {plan.UnresolvedCount} 项冲突需要选择处理方式。");
                if (plan.Request.TargetHashes != null)
                    foreach (var item in plan.Request.TargetHashes)
                        if (CurrentHash(TargetPath(next, item.Key)) != item.Value)
                            throw new IOException("游戏资源在预览期间又发生了变化，请放弃本次预览后重新同步。");
                var sourceBefore = Copy(Language(next, next.SourceLanguage).Working);
                foreach (var entry in plan.Entries)
                {
                    var values = Language(next, entry.Language).Working;
                    if (entry.MergedValue is { } value) values[entry.Key] = value;
                    else values.Remove(entry.Key);
                }
                var source = Language(next, next.SourceLanguage).Working;
                var incomingSource = plan.Request.Incoming.FirstOrDefault(l => l.Name == next.SourceLanguage)?.Values;
                var updatedTranslations = plan.Entries.Where(change => change.Language != next.SourceLanguage && change.MergedValue != change.Current)
                    .Select(change => (change.Language, change.Key)).ToHashSet();
                var removed = sourceBefore.Keys.Where(key => !source.ContainsKey(key)).ToHashSet(StringComparer.Ordinal);
                if (plan.Entries.Any(entry => entry.Language != next.SourceLanguage && removed.Contains(entry.Key) && entry.MergedValue != null))
                    throw new InvalidOperationException("有译文选择了保留，但它的源 Key 选择了删除。请保留源 Key，或将对应译文也选择为传入的删除。");
                foreach (var language in next.Languages.Where(l => l.Name != next.SourceLanguage))
                {
                    foreach (var key in removed) language.Working.Remove(key);
                    language.RetranslateKeys.RemoveWhere(key => !source.ContainsKey(key));
                    foreach (var entry in source)
                    {
                        var changedSource = sourceBefore.TryGetValue(entry.Key, out var before) && before != entry.Value;
                        var importedForOtherSource = incomingSource != null && incomingSource.TryGetValue(entry.Key, out var incomingText) && incomingText != entry.Value &&
                            updatedTranslations.Contains((language.Name, entry.Key));
                        if (language.Working.TryGetValue(entry.Key, out var translated) && !string.IsNullOrWhiteSpace(translated) &&
                            (importedForOtherSource || (changedSource && !updatedTranslations.Contains((language.Name, entry.Key)))))
                            language.RetranslateKeys.Add(entry.Key);
                    }
                }
                if (plan.Request.TargetHashes != null)
                    foreach (var incoming in plan.Request.Incoming)
                    {
                        var language = Language(next, incoming.Name);
                        language.Baseline = Copy(incoming.Values);
                        language.BaselineHash = plan.Request.TargetHashes[incoming.Name];
                    }
                next.PendingMerge = null;
                var beforeMerge = Clone(document);
                beforeMerge.PendingMerge = null;
                var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.locproj";
                IOUtils.AtomicWriteAllText(Path.Combine(HistoryDirectory, name), JsonConvert.SerializeObject(beforeMerge, Formatting.Indented));
                next.LastMergeBackup = name;
                next.LastMergeState = MergeStateHash(next);
            });
            if (plan.Request.HistoryKind is { } kind)
                lock (sync) RecordVersions(kind, plan.Request.Incoming, document.LastMergeBackup);
        }

        public void UndoLastMerge()
        {
            string undone;
            lock (sync)
            {
                using var lease = AcquireProjectLock();
                if (document.LastMergeBackup == null) throw new InvalidOperationException("没有可撤销的合并。");
                if (document.PendingMerge != null || document.LastMergeState != MergeStateHash(document))
                    throw new InvalidOperationException("合并后已有其他修改或发布，不能直接撤销。合并前的副本仍保存在项目旁的 .history 目录中。");
                undone = document.LastMergeBackup;
                var before = JsonConvert.DeserializeObject<ProjectDocument>(File.ReadAllText(Path.Combine(HistoryDirectory, undone)))
                    ?? throw new InvalidDataException("合并前的副本无效。");
                Validate(before);
                if (before.Id != document.Id || before.TargetDirectory != document.TargetDirectory)
                    throw new InvalidDataException("合并前的副本不属于当前项目。");
                Commit(before);
                // The undone file must not become the base for the next import of the same file.
                ForgetVersions(undone);
            }
            Changed?.Invoke();
        }

        private MergePlan BuildMergePlan(MergeRequest request)
        {
            var entries = new List<MergeEntry>();
            var choices = request.Choices.ToDictionary(choice => (choice.Language, choice.Key));
            var incomingSource = request.Incoming.FirstOrDefault(l => l.Name == document.SourceLanguage);
            var baseSource = request.Basis?.FirstOrDefault(l => l.Name == document.SourceLanguage);
            var removedSource = baseSource?.Values.Keys.Where(key => incomingSource != null && !incomingSource.Values.ContainsKey(key))
                .ToHashSet(StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            foreach (var incoming in request.Incoming)
            {
                var current = Language(document, incoming.Name).Working;
                var basis = request.Basis?.FirstOrDefault(l => l.Name == incoming.Name)?.Values;
                var keys = basis == null ? incoming.Values.Keys.AsEnumerable() : basis.Keys.Union(incoming.Values.Keys, StringComparer.Ordinal)
                    .Union(current.Keys.Where(removedSource.Contains), StringComparer.Ordinal);
                foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    current.TryGetValue(key, out var local);
                    incoming.Values.TryGetValue(key, out var remote);
                    string? original = null;
                    basis?.TryGetValue(key, out original);
                    var deletesSource = incoming.Name != document.SourceLanguage && removedSource.Contains(key);
                    if (deletesSource) remote = null;
                    if (local == remote || (basis != null && remote == original && !deletesSource)) continue;
                    var conflict = basis == null ? local != null : local != original;
                    var entry = new MergeEntry
                    {
                        Language = incoming.Name,
                        Key = key,
                        Before = original,
                        Current = local,
                        Incoming = remote,
                        IsConflict = conflict,
                        Result = remote ?? string.Empty,
                        Resolution = conflict ? MergeResolution.Unresolved : MergeResolution.UseIncoming,
                    };
                    if (choices.TryGetValue((entry.Language, entry.Key), out var previous) && previous.Current == entry.Current &&
                        previous.Incoming == entry.Incoming && previous.Before == entry.Before)
                    { entry.Result = previous.Result; entry.Resolution = previous.Resolution; }
                    entries.Add(entry);
                }
            }
            return new MergePlan(document.Id, fileHash, revision, request, entries);
        }

        // Pick the recorded version the incoming file was most likely edited from: the most unchanged
        // values, minus the keys it would delete, so a stale or newer export is not mistaken for the base.
        private static (LanguageVersion Version, bool Partial)? MatchBase(Dictionary<string, string> incoming, IEnumerable<LanguageVersion> candidates)
        {
            (LanguageVersion Version, bool Partial)? best = null;
            var bestScore = int.MinValue;
            foreach (var candidate in candidates)
            {
                int shared = 0, same = 0;
                foreach (var pair in candidate.Values)
                    if (incoming.TryGetValue(pair.Key, out var value)) { shared++; if (value == pair.Value) same++; }
                // Unrelated files share few keys with every version; merging against them would invent deletions.
                if (shared == 0 || (shared * 2 < incoming.Count && shared * 2 < candidate.Values.Count)) continue;
                var missing = candidate.Values.Count - shared;
                var partial = missing > shared;
                var score = same - (partial ? 0 : missing);
                if (best == null || score > bestScore || (score == bestScore && candidate.Time > best.Value.Version.Time))
                {
                    best = (candidate, partial);
                    bestScore = score;
                }
            }
            return best;
        }

        private List<LanguageVersion> Candidates(ProjectLanguage language)
        {
            var versions = LoadVersions(language.Name);
            // Projects created before version history only know the last exported file.
            if (language.Baseline.Count > 0 && !versions.Any(v => Same(v.Values, language.Baseline)))
                versions.Add(new LanguageVersion { Language = language.Name, Kind = "上次导出", Time = DateTime.MaxValue, Values = Copy(language.Baseline) });
            return versions;
        }

        private static string Describe(LanguageVersion version) => version.Kind switch
        {
            "上次导出" => "上次导出的版本",
            "创建项目" => $"创建项目时（{version.Time:M月d日 HH:mm}）的版本",
            "导出" => $"{version.Time:M月d日 HH:mm} 导出的版本",
            "同步" => $"{version.Time:M月d日 HH:mm} 同步的游戏资源",
            _ => $"{version.Time:M月d日 HH:mm} 收到的文件",
        };

        private List<LanguageVersion> LoadVersions(string language) => LoadVersionFiles(language).Select(v => v.Version).ToList();

        private IEnumerable<(string Path, LanguageVersion Version)> LoadVersionFiles(string language)
        {
            var directory = VersionDirectory(language);
            if (!Directory.Exists(directory)) yield break;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json").ToArray())
            {
                LanguageVersion? version = null;
                try { version = JsonConvert.DeserializeObject<LanguageVersion>(File.ReadAllText(file)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                {
                    LogUtils.LogError($"跳过无法读取的版本记录 {file}：{error.Message}");
                }
                if (version?.Values != null && version.ProjectId == document.Id && version.Language == language &&
                    version.Values.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value != null))
                {
                    version.Values = Copy(version.Values);
                    yield return (file, version);
                }
            }
        }

        // History only improves later merges, so a failure here must not undo a completed publish or merge.
        private void RecordVersions(string kind, IEnumerable<LanguageSnapshot> snapshots, string? mergeBackup = null)
        {
            try
            {
                var time = DateTime.Now;
                foreach (var snapshot in snapshots.Where(s => s.Values.Count > 0))
                {
                    var directory = VersionDirectory(snapshot.Name);
                    Directory.CreateDirectory(directory);
                    var version = new LanguageVersion
                    {
                        ProjectId = document.Id,
                        Language = snapshot.Name,
                        Kind = kind,
                        Time = time,
                        MergeBackup = mergeBackup,
                        Values = snapshot.Values,
                    };
                    IOUtils.AtomicWriteAllText(Path.Combine(directory, $"{time:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json"), JsonConvert.SerializeObject(version));
                    foreach (var old in Directory.EnumerateFiles(directory, "*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                        .Skip(MaxVersionsPerLanguage))
                        File.Delete(old);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LogUtils.LogError($"记录文案版本失败，之后导入时可能需要手动确认更多差异：{error.Message}");
            }
        }

        private void ForgetVersions(string mergeBackup)
        {
            foreach (var language in document.Languages)
                foreach (var version in LoadVersionFiles(language.Name).Where(v => v.Version.MergeBackup == mergeBackup))
                    try { File.Delete(version.Path); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        LogUtils.LogError($"无法移除已撤销合并的版本记录 {version.Path}：{error.Message}");
                    }
        }

        private void CheckMergePlan(MergePlan plan)
        {
            if (plan.ProjectId != document.Id || plan.StateHash != fileHash || plan.Revision != revision)
                throw new InvalidOperationException("项目在预览后已有修改，请重新打开合并预览；你的编辑已保留。");
        }

        private void EnsureNoPendingMerge()
        {
            if (document.PendingMerge != null) throw new InvalidOperationException("已有保存的合并进度，请先点击“继续合并”处理，或在项目操作中放弃本次合并。");
        }

        private static MergeRequest CloneRequest(MergeRequest request) => JsonConvert.DeserializeObject<MergeRequest>(JsonConvert.SerializeObject(request))!;

        private static string MergeStateHash(ProjectDocument data)
        {
            var copy = Clone(data);
            copy.LastMergeBackup = null;
            copy.LastMergeState = null;
            return Hash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(copy)));
        }

        private static void ValidateMerge(ProjectDocument data)
        {
            void ValidateSnapshots(List<LanguageSnapshot> snapshots)
            {
                if (snapshots.Any(l => l == null || l.Values == null ||
                    l.Values.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null) || !data.Languages.Any(current => current.Name == l.Name)) ||
                    snapshots.Select(l => l.Name).Distinct(StringComparer.Ordinal).Count() != snapshots.Count)
                    throw new InvalidDataException("合并进度中的语言或文案无效。");
            }
            if (data.PendingMerge is { } pending)
            {
                if (pending.Incoming == null || pending.Choices == null || pending.Notes == null) throw new InvalidDataException("合并进度无效。");
                ValidateSnapshots(pending.Incoming);
                if (pending.Basis != null) ValidateSnapshots(pending.Basis);
                if (pending.Choices.Any(choice => choice == null || !Enum.IsDefined(choice.Resolution) || choice.Result == null))
                    throw new InvalidDataException("合并选择无效。");
                if (pending.TargetHashes != null && (pending.TargetHashes.Count != pending.Incoming.Count ||
                    pending.Incoming.Any(l => !pending.TargetHashes.ContainsKey(l.Name)))) throw new InvalidDataException("同步信息无效。");
            }
            if (data.LastMergeBackup != null && (Path.GetFileName(data.LastMergeBackup) != data.LastMergeBackup ||
                data.LastMergeBackup.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw new InvalidDataException("合并备份路径无效。");
        }
    }
}
