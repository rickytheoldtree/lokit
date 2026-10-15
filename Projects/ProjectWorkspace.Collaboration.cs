using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LocalizationTranslationWPF.Projects
{
    public sealed partial class ProjectWorkspace
    {
        public void ExportCollaboration(string path)
        {
            lock (sync)
            {
                RefreshWorkspaceFromDisk();
                if (document.PendingMerge != null) throw new InvalidOperationException("请先完成或放弃当前合并，再发送协作副本。");
                if (CurrentHash(FilePath) != fileHash) throw new IOException("项目文件已变化，请先接收项目文件中的最新修改。");
                if (string.Equals(Path.GetFullPath(path), FilePath, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("请选择另一个文件名，保留当前项目作为自己的工作副本。");
                var share = Clone(document);
                share.TargetDirectory = string.Empty;
                share.LastBackup = null;
                share.LastMergeBackup = null;
                share.LastMergeState = null;
                share.ReceivedCollaborations.Clear();
                share.ReceivedImports.Clear();
                share.CollaborationId ??= Guid.NewGuid().ToString("N");
                share.CollaborationBase ??= Snapshot(document);
                foreach (var language in share.Languages)
                {
                    language.Baseline = Copy(language.Working);
                    language.BaselineHash = null;
                }
                Validate(share);
                Utils.IOUtils.AtomicWriteAllText(path, JsonConvert.SerializeObject(share, Formatting.Indented));
            }
        }

        public MergePlan PreviewCollaboration(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var incoming = JsonConvert.DeserializeObject<ProjectDocument>(Decode(bytes))
                ?? throw new InvalidDataException("协作文件为空。");
            Validate(incoming);
            lock (sync)
            {
                RefreshWorkspaceFromDisk();
                EnsureNoPendingMerge();
                if (incoming.Id != document.Id || incoming.SourceLanguage != document.SourceLanguage ||
                    incoming.ConverterType != document.ConverterType || incoming.Languages.Count != document.Languages.Count ||
                    incoming.Languages.Any(l => !document.Languages.Any(current => current.Name == l.Name && current.IsoCode == l.IsoCode)))
                    throw new InvalidDataException("这个文件属于另一个项目，或语言配置不同。请打开对应项目后再接收修改。");
                var values = Snapshot(incoming);
                var receipt = incoming.CollaborationId == null ? null : incoming.CollaborationId + ":" + SnapshotHash(values);
                var basis = incoming.CollaborationId != null && document.ReceivedCollaborations.TryGetValue(incoming.CollaborationId, out var previous)
                    ? previous : incoming.CollaborationBase;
                return BuildMergePlan(new MergeRequest
                {
                    Title = Path.GetFileName(path),
                    Incoming = receipt != null && document.ReceivedImports.Contains(receipt) ? new() : values,
                    Basis = basis,
                    CollaborationId = incoming.CollaborationId,
                    Receipt = receipt,
                });
            }
        }

        public MergePlan PreviewLanguageImport(IEnumerable<KeyValuePair<string, string>> languageFiles)
        {
            lock (sync)
            {
                RefreshWorkspaceFromDisk();
                EnsureNoPendingMerge();
                var request = new MergeRequest { Title = "导入文案" };
                var names = new HashSet<string>(StringComparer.Ordinal);
                var converter = CreateConverter(ConverterType);
                foreach (var file in languageFiles)
                {
                    _ = Language(document, file.Key);
                    if (!names.Add(file.Key)) throw new InvalidDataException($"一次只能导入一个“{file.Key}”文件，请分别接收这些修订。");
                    var bytes = File.ReadAllBytes(file.Value);
                    // Reject duplicate properties before a converter can silently discard a value.
                    JToken.Parse(Decode(bytes), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    var values = Parse(bytes, converter, file.Value);
                    if (values.Keys.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("导入文件的 Key 不能为空。");
                    request.Incoming.Add(new LanguageSnapshot { Name = file.Key, Values = values });
                }
                if (request.Incoming.Count == 0) throw new InvalidDataException("没有可导入的语言文件。");
                return BuildMergePlan(request);
            }
        }

        public MergePlan PreviewTargetSync()
        {
            lock (sync)
            {
                RefreshWorkspaceFromDisk();
                EnsureNoPendingMerge();
                if (!HasPublishTarget) throw new InvalidOperationException("协作副本无需同步游戏目录，请接收协作文件。");
                var converter = CreateConverter(ConverterType);
                var request = new MergeRequest { Title = "同步游戏资源", Basis = new(), TargetHashes = new(StringComparer.Ordinal) };
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
                if (plan.Request.CollaborationId != null && plan.Request.Incoming.Count > 0)
                    next.ReceivedCollaborations[plan.Request.CollaborationId] = plan.Request.Incoming;
                if (plan.Request.Receipt != null) next.ReceivedImports.Add(plan.Request.Receipt);
                next.PendingMerge = null;
                var beforeMerge = Clone(document);
                beforeMerge.PendingMerge = null;
                var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.locproj";
                Utils.IOUtils.AtomicWriteAllText(Path.Combine(FilePath + ".history", name), JsonConvert.SerializeObject(beforeMerge, Formatting.Indented));
                next.LastMergeBackup = name;
                next.LastMergeState = MergeStateHash(next);
            });
        }

        public void UndoLastMerge()
        {
            lock (sync)
            {
                using var lease = AcquireProjectLock();
                if (document.LastMergeBackup == null) throw new InvalidOperationException("没有可撤销的合并。");
                if (document.PendingMerge != null || document.LastMergeState != MergeStateHash(document))
                    throw new InvalidOperationException("合并后已有其他修改或发布，不能直接撤销。合并前的副本仍保存在项目旁的 .history 目录中。");
                var before = JsonConvert.DeserializeObject<ProjectDocument>(File.ReadAllText(Path.Combine(FilePath + ".history", document.LastMergeBackup)))
                    ?? throw new InvalidDataException("合并前的副本无效。");
                Validate(before);
                if (before.Id != document.Id || before.TargetDirectory != document.TargetDirectory)
                    throw new InvalidDataException("合并前的副本不属于当前项目。");
                Commit(before);
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
                        Language = incoming.Name, Key = key, Before = original, Current = local, Incoming = remote,
                        IsConflict = conflict, Result = remote ?? string.Empty,
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

        private void CheckMergePlan(MergePlan plan)
        {
            if (plan.ProjectId != document.Id || plan.StateHash != fileHash || plan.Revision != revision)
                throw new InvalidOperationException("项目在预览后已有修改，请重新打开合并预览；你的编辑已保留。");
        }

        private void EnsureNoPendingMerge()
        {
            if (document.PendingMerge != null) throw new InvalidOperationException("已有保存的合并进度，请先点击“继续合并”处理，或在项目操作中放弃本次合并。");
        }

        private static List<LanguageSnapshot> Snapshot(ProjectDocument data) => data.Languages.Select(language =>
            new LanguageSnapshot { Name = language.Name, Values = Copy(language.Working) }).ToList();

        private static string SnapshotHash(List<LanguageSnapshot> snapshots) => Hash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
            snapshots.OrderBy(l => l.Name, StringComparer.Ordinal).Select(l => new
            {
                l.Name,
                Values = l.Values.OrderBy(p => p.Key, StringComparer.Ordinal),
            }))));

        private static MergeRequest CloneRequest(MergeRequest request) => JsonConvert.DeserializeObject<MergeRequest>(JsonConvert.SerializeObject(request))!;

        private static string MergeStateHash(ProjectDocument data)
        {
            var copy = Clone(data);
            copy.LastMergeBackup = null;
            copy.LastMergeState = null;
            return Hash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(copy)));
        }

        private static void ValidateCollaboration(ProjectDocument data)
        {
            if (data.ReceivedCollaborations == null || data.ReceivedImports == null ||
                (data.CollaborationBase == null) != (data.CollaborationId == null) || data.CollaborationId == string.Empty)
                throw new InvalidDataException("项目的协作信息无效。");
            void ValidateSnapshots(List<LanguageSnapshot> snapshots, bool complete)
            {
                if ((complete && snapshots.Count != data.Languages.Count) || snapshots.Any(l => l == null || l.Values == null ||
                    l.Values.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null) || !data.Languages.Any(current => current.Name == l.Name)) ||
                    snapshots.Select(l => l.Name).Distinct(StringComparer.Ordinal).Count() != snapshots.Count)
                    throw new InvalidDataException("协作文件中的语言或文案无效。");
            }
            if (data.CollaborationBase != null) ValidateSnapshots(data.CollaborationBase, true);
            foreach (var snapshots in data.ReceivedCollaborations.Values) ValidateSnapshots(snapshots, true);
            if (data.PendingMerge is { } pending)
            {
                if (pending.Incoming == null || pending.Choices == null) throw new InvalidDataException("合并进度无效。");
                ValidateSnapshots(pending.Incoming, false);
                if (pending.Basis != null) ValidateSnapshots(pending.Basis, false);
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
