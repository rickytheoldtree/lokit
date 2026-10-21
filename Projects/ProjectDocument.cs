using Lokit.Data;

namespace Lokit.Projects
{
    public sealed class ProjectDocument
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "本地化项目";
        public string TargetDirectory { get; set; } = string.Empty;
        public string ConverterType { get; set; } = string.Empty;
        public string SourceLanguage { get; set; } = string.Empty;
        public List<ProjectLanguage> Languages { get; set; } = new();
        public string? LastBackup { get; set; }
        public MergeRequest? PendingMerge { get; set; }
        public string? LastMergeBackup { get; set; }
        public string? LastMergeState { get; set; }
    }

    public sealed class ProjectLanguage
    {
        public string Name { get; set; } = string.Empty;
        public string IsoCode { get; set; } = string.Empty;
        public Dictionary<string, string> Working { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Baseline { get; set; } = new(StringComparer.Ordinal);
        public string? BaselineHash { get; set; }
        public HashSet<string> RetranslateKeys { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed record EntryChange(string Language, string Key, string Kind, string? Before, string? After);

    public sealed class PublishPlan
    {
        internal PublishPlan(string projectId, string stateHash, long revision, List<EntryChange> changes, List<string> conflicts, List<string> files)
        {
            ProjectId = projectId;
            StateHash = stateHash;
            Revision = revision;
            Changes = changes.AsReadOnly();
            Conflicts = conflicts.AsReadOnly();
            Files = files.AsReadOnly();
        }
        internal long Revision { get; }
        internal string ProjectId { get; }
        internal string StateHash { get; }
        public IReadOnlyList<EntryChange> Changes { get; }
        public IReadOnlyList<string> Conflicts { get; }
        public IReadOnlyList<string> Files { get; }
        public bool CanPublish => Files.Count > 0 && Conflicts.Count == 0;
    }

    public sealed record TranslationInput(string ProjectId, string TargetLanguage,
        Dictionary<string, string> Source, Dictionary<string, string> TargetBefore,
        HashSet<string> RetranslateBefore);

    internal sealed class PublishBackup
    {
        public string ProjectId { get; set; } = string.Empty;
        public string TargetDirectory { get; set; } = string.Empty;
        public List<BackupFile> Files { get; set; } = new();
    }

    internal sealed class BackupFile
    {
        public string Language { get; set; } = string.Empty;
        public byte[]? Before { get; set; }
        public string? PublishedHash { get; set; }
    }

    // A language file the project exported or received. JSON edited outside the tool is merged
    // against the recorded version it most likely started from.
    internal sealed class LanguageVersion
    {
        public string ProjectId { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public string? MergeBackup { get; set; }
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    }

    // Only the application's current selection is global; project data and file operations live
    // in independently testable ProjectWorkspace instances with no WPF references.
    public static class ProjectContext
    {
        public static ProjectWorkspace? Current { get; set; }
        public static List<Pair> GetPairs() => Current?.Pairs.ToList() ??
            Utils.PairUtils.GetPairs(Utils.ConfigUtils.IsoPairIndex.Value);
    }
}
