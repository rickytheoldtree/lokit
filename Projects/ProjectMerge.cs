using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace LocalizationTranslationWPF.Projects
{
    public sealed class LanguageSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    }

    public enum MergeResolution { Unresolved, KeepCurrent, UseIncoming, Custom }

    public sealed class EditorMergeConflictException : InvalidOperationException
    {
        public EditorMergeConflictException(MergePlan plan) : base("部分文案已被其他人修改。你的编辑可保存为合并进度，然后逐项选择。") => Plan = plan;
        public MergePlan Plan { get; }
    }

    public sealed class MergeEntry : INotifyPropertyChanged
    {
        private MergeResolution resolution;
        private string result = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string? Before { get; set; }
        public string? Current { get; set; }
        public string? Incoming { get; set; }
        public bool IsConflict { get; set; }
        [JsonIgnore] public string Kind => IsConflict ? "冲突" : Incoming == null ? "删除" : Current == null ? "新增" : "修改";
        [JsonIgnore] public string BeforeText => Before ?? "（无）";
        [JsonIgnore] public string CurrentText => Current ?? "（不存在）";
        [JsonIgnore] public string IncomingText => Incoming ?? "（删除 / 不存在）";
        public MergeResolution Resolution
        {
            get => resolution;
            set { resolution = value; Notify(); }
        }
        public string Result
        {
            get => result;
            set { result = value; Notify(); }
        }
        [JsonIgnore]
        public string? MergedValue => Resolution switch
        {
            MergeResolution.KeepCurrent => Current,
            MergeResolution.UseIncoming => Incoming,
            MergeResolution.Custom => Result,
            _ => throw new InvalidOperationException("请先处理全部冲突。"),
        };
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // The request is persisted when the user chooses to finish later. Rebuild its preview
    // against the current workspace before resuming, preserving only still-valid choices.
    public sealed class MergeRequest
    {
        public string Title { get; set; } = string.Empty;
        public List<LanguageSnapshot>? Basis { get; set; }
        public List<LanguageSnapshot> Incoming { get; set; } = new();
        // Set for merges that bring in a file version worth remembering as a future merge base.
        public string? HistoryKind { get; set; }
        public List<string> Notes { get; set; } = new();
        public Dictionary<string, string?>? TargetHashes { get; set; }
        public List<MergeEntry> Choices { get; set; } = new();
    }

    public sealed class MergePlan
    {
        internal MergePlan(string projectId, string stateHash, long revision, MergeRequest request, List<MergeEntry> entries)
        {
            ProjectId = projectId;
            StateHash = stateHash;
            Revision = revision;
            Request = request;
            Entries = entries.AsReadOnly();
        }
        internal string ProjectId { get; }
        internal string StateHash { get; }
        internal long Revision { get; }
        internal MergeRequest Request { get; }
        public string Title => Request.Title;
        public IReadOnlyList<string> Notes => Request.Notes;
        public IReadOnlyList<MergeEntry> Entries { get; }
        public int ConflictCount => Entries.Count(e => e.IsConflict);
        public int UnresolvedCount => Entries.Count(e => e.Resolution == MergeResolution.Unresolved);
        public bool IsTargetSync => Request.TargetHashes != null;
    }
}
