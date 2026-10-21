using System.IO;
using Lokit.Projects;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // Files someone else edited live outside the project folder, like JSON sent back over chat.
    private static string IncomingPath(ProjectWorkspace project, string language)
    {
        var directory = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "incoming");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, language + ".json");
    }

    private static void Change(ProjectWorkspace project, string language, string key, string value)
    {
        var before = project.ReadLanguage(language);
        project.SaveLanguage(language, new(before, StringComparer.Ordinal) { [key] = value }, before);
    }

    private static void ResolveIncoming(MergePlan plan)
    {
        foreach (var entry in plan.Entries) entry.Resolution = MergeResolution.UseIncoming;
    }

    private static MergePlan Import(ProjectWorkspace project, Dictionary<string, string> values, string language = "English") =>
        ImportFiles(project, new() { [language] = values });

    private static MergePlan ImportFiles(ProjectWorkspace project, Dictionary<string, Dictionary<string, string>> files)
    {
        var mapped = new List<KeyValuePair<string, string>>();
        foreach (var file in files)
        {
            var path = IncomingPath(project, file.Key);
            Write(path, file.Value);
            mapped.Add(new(file.Key, path));
        }
        return project.PreviewLanguageImport(mapped);
    }

    private static void PublishAll(ProjectWorkspace project) => project.Publish(project.PreviewPublish());

    private static void JsonBaseMerge()
    {
        var project = CreateProject();
        project.AddSourceEntries(new() { ["keep"] = "Keep", ["e1"] = "E1", ["e2"] = "E2" });
        PublishAll(project);
        var exported = Read(Target(project, "English"));
        // Someone edits the exported file while the project keeps changing.
        var dev = new Dictionary<string, string>(exported) { ["base"] = "Dev {0}", ["e1"] = "E1 dev", ["e2"] = "E2 dev", ["dev-new"] = "Dev new" };
        project.AddSourceEntries(new() { ["product-new"] = "Product new" });
        Change(project, "English", "keep", "Product keep");
        var plan = Import(project, dev);
        Equal(0, plan.ConflictCount);
        Equal(4, plan.Entries.Count);
        True(plan.Notes.Single().Contains("导出的版本", StringComparison.Ordinal));
        project.ApplyMerge(plan);
        var english = project.ReadLanguage("English");
        Equal("Dev {0}", english["base"]);
        Equal("Dev new", english["dev-new"]);
        Equal("Product keep", english["keep"]);
        True(english.ContainsKey("product-new"));
        Equal(1, project.PendingRetranslationCount);
        Equal("Hello {0}", Read(Target(project, "English"))["base"]);
        // The same file again changes nothing; a follow-up edit of it only shows the new edits.
        Equal(0, Import(project, dev).Entries.Count);
        var next = new Dictionary<string, string>(dev) { ["base"] = "Dev again {0}" };
        next.Remove("dev-new");
        plan = Import(project, next);
        Equal(0, plan.ConflictCount);
        Equal(2, plan.Entries.Count);
        True(plan.Notes.Single().Contains("收到的文件", StringComparison.Ordinal));
        Equal("删除", plan.Entries.Single(e => e.Key == "dev-new").Kind);
        project.ApplyMerge(plan);
        True(!project.ReadLanguage("English").ContainsKey("dev-new"));
        Change(project, "English", "base", "Product {0}");
        plan = Import(project, new(next) { ["base"] = "Dev third {0}" });
        Equal(1, plan.ConflictCount);
        var conflict = plan.Entries.Single(e => e.IsConflict);
        Equal("Dev again {0}", conflict.Before);
        Equal("Product {0}", conflict.Current);
        Throws<InvalidOperationException>(() => project.ApplyMerge(plan));
        conflict.Resolution = MergeResolution.KeepCurrent;
        project.ApplyMerge(plan);
        Equal("Product {0}", project.ReadLanguage("English")["base"]);
        // A choice made once is remembered through the received file.
        Equal(0, Import(project, new(next) { ["base"] = "Dev third {0}" }).Entries.Count);
    }

    private static void StaleAndPartialFiles()
    {
        var project = CreateProject();
        project.AddSourceEntries(new() { ["a"] = "A", ["b"] = "B", ["c"] = "C" });
        PublishAll(project);
        var first = Read(Target(project, "English"));
        Change(project, "English", "a", "A2");
        project.AddSourceEntries(new() { ["d"] = "D" });
        PublishAll(project);
        // Edited from the first export: matching it keeps the newer "A2" and "d" instead of reverting or deleting them.
        var plan = Import(project, new(first) { ["b"] = "B dev" });
        Equal(0, plan.ConflictCount);
        Equal("b", plan.Entries.Single().Key);
        project.ApplyMerge(plan);
        var english = project.ReadLanguage("English");
        Equal("A2", english["a"]);
        Equal("B dev", english["b"]);
        True(english.ContainsKey("d"));
        // A file with only a few keys updates those keys and deletes nothing.
        plan = Import(project, new() { ["b"] = "B partial" });
        Equal(0, plan.ConflictCount);
        Equal("b", plan.Entries.Single().Key);
        True(plan.Notes.Single().Contains("部分 Key", StringComparison.Ordinal));
        project.ApplyMerge(plan);
        Equal(5, project.ReadLanguage("English").Count);
        // An unrelated file has no trustworthy base: additions merge, differing values need a choice, nothing is deleted.
        plan = Import(project, new() { ["x"] = "X", ["y"] = "Y", ["z"] = "Z", ["a"] = "Other" });
        True(plan.Notes.Single().Contains("没有找到", StringComparison.Ordinal));
        Equal(1, plan.ConflictCount);
        Equal(4, plan.Entries.Count);
        True(plan.Entries.All(e => e.Incoming != null));
    }

    private static void LanguageImport()
    {
        var project = CreateProject();
        project.AddSourceEntries(new() { ["keep"] = "Keep" });
        Change(project, "English", "base", "Local {0}");
        var plan = Import(project, new() { ["base"] = "Changed {0}", ["new"] = "New", ["empty"] = "" });
        Equal(1, plan.ConflictCount);
        Throws<InvalidOperationException>(() => project.ApplyMerge(plan));
        True(!project.ReadLanguage("English").ContainsKey("new"));
        ResolveIncoming(plan);
        project.ApplyMerge(plan);
        True(project.ReadLanguage("English").ContainsKey("keep"));
        Equal("", project.ReadLanguage("English")["empty"]);
        Equal(1, project.PendingRetranslationCount);
        Equal("你好 {0}", project.ReadLanguage("ChineseSimplified")["base"]);
        Equal("Hello {0}", Read(Target(project, "English"))["base"]);
        var file = IncomingPath(project, "English");
        File.WriteAllText(file, "{\"same\":\"one\",\"same\":\"two\"}");
        Throws<Newtonsoft.Json.JsonReaderException>(() => project.PreviewLanguageImport(new[] { new KeyValuePair<string, string>("English", file) }));
    }

    private static void MergeDeletion()
    {
        static ProjectWorkspace Exported()
        {
            var project = CreateProject();
            project.AddSourceEntries(new() { ["other"] = "Other" });
            Change(project, "ChineseSimplified", "other", "其他");
            PublishAll(project);
            return project;
        }
        var withoutBase = new Dictionary<string, Dictionary<string, string>>
        {
            ["English"] = new() { ["other"] = "Other" },
            ["ChineseSimplified"] = new() { ["other"] = "其他" },
        };
        var project = Exported();
        Change(project, "ChineseSimplified", "base", "我的新译文 {0}");
        var plan = ImportFiles(project, withoutBase);
        Equal(1, plan.ConflictCount);
        Equal("删除", plan.Entries.Single(e => e.Language == "English").Kind);
        plan.Entries.Single(e => e.IsConflict).Resolution = MergeResolution.KeepCurrent;
        Throws<InvalidOperationException>(() => project.ApplyMerge(plan));
        True(project.ReadLanguage("English").ContainsKey("base"));
        plan.Entries.Single(e => e.Language == "English").Resolution = MergeResolution.KeepCurrent;
        project.ApplyMerge(plan);
        Equal("我的新译文 {0}", project.ReadLanguage("ChineseSimplified")["base"]);
        True(project.ReadLanguage("English").ContainsKey("base"));
        project = Exported();
        plan = ImportFiles(project, withoutBase);
        Equal(0, plan.ConflictCount);
        project.ApplyMerge(plan);
        True(project.Pairs.All(pair => !project.ReadLanguage(pair.Name).ContainsKey("base")));
    }

    private static void MergeDraft()
    {
        var owner = CreateProject();
        Change(owner, "English", "base", "Local {0}");
        var plan = Import(owner, new() { ["base"] = "Imported {0}", ["new"] = "New" });
        var conflict = plan.Entries.Single(e => e.IsConflict);
        conflict.Result = "手工合并 {0}";
        conflict.Resolution = MergeResolution.Custom;
        owner.SaveMergeDraft(plan);
        owner = ProjectWorkspace.Open(owner.FilePath);
        True(owner.HasPendingMerge);
        True(!owner.PreviewPublish().CanPublish);
        owner.AddSourceEntries(new() { ["meanwhile"] = "Other work" });
        plan = owner.PreviewPendingMerge();
        True(plan.Notes.Single().Contains("创建项目时", StringComparison.Ordinal));
        conflict = plan.Entries.Single(e => e.IsConflict);
        Equal(MergeResolution.Custom, conflict.Resolution);
        Equal("手工合并 {0}", conflict.Result);
        owner.ApplyMerge(plan);
        True(!owner.HasPendingMerge);
        Equal("手工合并 {0}", owner.ReadLanguage("English")["base"]);
        True(owner.ReadLanguage("English").ContainsKey("meanwhile"));
        True(owner.ReadLanguage("English").ContainsKey("new"));
    }

    private static void MergeHistory()
    {
        var owner = CreateProject();
        var plan = Import(owner, new() { ["base"] = "Hello {0}", ["new"] = "New" });
        owner.AddSourceEntries(new() { ["meanwhile"] = "Other work" });
        Throws<InvalidOperationException>(() => owner.ApplyMerge(plan));
        True(!owner.ReadLanguage("English").ContainsKey("new"));
        owner.ApplyMerge(owner.RefreshMergePlan(plan));
        owner = ProjectWorkspace.Open(owner.FilePath);
        True(owner.HasMergeBackup);
        owner.UndoLastMerge();
        True(!owner.ReadLanguage("English").ContainsKey("new"));
        True(owner.ReadLanguage("English").ContainsKey("meanwhile"));
        // The undone file is forgotten, so receiving it again still offers its changes.
        plan = Import(owner, new() { ["base"] = "Hello {0}", ["new"] = "New" });
        Equal(1, plan.Entries.Count);
        owner.ApplyMerge(plan);
        owner.AddSourceEntries(new() { ["later"] = "Later" });
        Throws<InvalidOperationException>(() => owner.UndoLastMerge());
        True(owner.ReadLanguage("English").ContainsKey("later"));
    }

    private static void ResourceSync()
    {
        var owner = CreateProject();
        owner.AddSourceEntries(new() { ["mine"] = "Mine" });
        Write(Target(owner, "English"), new() { ["base"] = "Hello {0}", ["theirs"] = "Theirs" });
        True(!owner.PreviewPublish().CanPublish);
        var plan = owner.PreviewTargetSync();
        Equal(0, plan.ConflictCount);
        owner.ApplyMerge(plan);
        True(owner.ReadLanguage("English").ContainsKey("mine"));
        True(owner.ReadLanguage("English").ContainsKey("theirs"));
        var publish = owner.PreviewPublish();
        True(publish.CanPublish);
        Equal(1, publish.Changes.Count);
        owner.Publish(publish);
        True(Read(Target(owner, "English")).ContainsKey("mine"));
        Change(owner, "English", "base", "My source {0}");
        Write(Target(owner, "English"), new() { ["base"] = "Other source {0}", ["mine"] = "Mine", ["theirs"] = "Theirs" });
        plan = owner.PreviewTargetSync();
        Equal(1, plan.ConflictCount);
        ResolveIncoming(plan);
        File.AppendAllText(Target(owner, "English"), " ");
        Throws<IOException>(() => owner.ApplyMerge(plan));
        Equal("My source {0}", owner.ReadLanguage("English")["base"]);
        owner.ApplyMerge(owner.RefreshMergePlan(plan));
        Equal("Other source {0}", owner.ReadLanguage("English")["base"]);
        Equal(0, owner.PreviewPublish().Conflicts.Count);
    }

    private static void TamperedMergeData()
    {
        var project = CreateProject();
        var versions = Path.Combine(project.FilePath + ".history", "versions", "English");
        // Corrupt records and records of another project are ignored rather than used as a base.
        File.WriteAllText(Path.Combine(versions, "broken.json"), "{");
        var foreign = JObject.Parse(File.ReadAllText(Directory.EnumerateFiles(versions, "2*.json").Single()));
        foreign["ProjectId"] = "other";
        foreign["Values"] = new JObject { ["base"] = "Changed {0}" };
        File.WriteAllText(Path.Combine(versions, "foreign.json"), foreign.ToString());
        Equal(1, Import(project, new() { ["base"] = "Changed {0}" }).Entries.Count);
        var json = JObject.Parse(File.ReadAllText(project.FilePath));
        json["LastMergeBackup"] = "../other.locproj";
        File.WriteAllText(project.FilePath, json.ToString());
        Throws<InvalidDataException>(() => ProjectWorkspace.Open(project.FilePath));
    }
}
