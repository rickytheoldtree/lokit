using System.IO;
using LocalizationTranslationWPF.Projects;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static string SharePath(ProjectWorkspace project, string name = "Product.locproj") =>
        Path.Combine(Path.GetDirectoryName(project.FilePath)!, name);

    private static void Change(ProjectWorkspace project, string language, string key, string value)
    {
        var before = project.ReadLanguage(language);
        project.SaveLanguage(language, new(before, StringComparer.Ordinal) { [key] = value }, before);
    }

    private static void ResolveIncoming(MergePlan plan)
    {
        foreach (var entry in plan.Entries) entry.Resolution = MergeResolution.UseIncoming;
    }

    private static MergePlan Import(ProjectWorkspace project, Dictionary<string, string> values)
    {
        var file = SharePath(project, "English.json");
        Write(file, values);
        return project.PreviewLanguageImport(new[] { new KeyValuePair<string, string>("English", file) });
    }

    private static void CollaborationRoundtrip()
    {
        var owner = CreateProject();
        Change(owner, "ChineseSimplified", "base", "AI 你好 {0}");
        var share = SharePath(owner);
        owner.ExportCollaboration(share);
        var exported = File.ReadAllText(share);
        True(!exported.Contains(owner.TargetDirectory, StringComparison.Ordinal));
        True(!exported.Contains(owner.BackupDirectory, StringComparison.Ordinal));
        var product = ProjectWorkspace.Open(share);
        True(product.IsCollaborationCopy && !product.HasPublishTarget);
        True(!product.PreviewPublish().CanPublish);
        Equal("AI 你好 {0}", product.ReadLanguage("ChineseSimplified")["base"]);
        product.AddSourceEntries(new() { ["product-added"] = "Product text" });
        Change(product, "ChineseSimplified", "base", "产品你好 {0}");
        owner.AddSourceEntries(new() { ["owner-added"] = "Owner text" });
        Change(owner, "ChineseSimplified", "base", "负责人你好 {0}");
        var returned = SharePath(owner, "Return.locproj");
        product.ExportCollaboration(returned);
        var plan = owner.PreviewCollaboration(returned);
        Equal(1, plan.ConflictCount);
        True(!owner.ReadLanguage("English").ContainsKey("product-added"));
        var conflict = plan.Entries.Single(e => e.IsConflict);
        Equal("AI 你好 {0}", conflict.Before);
        conflict.Resolution = MergeResolution.KeepCurrent;
        owner.ApplyMerge(plan);
        Equal("负责人你好 {0}", owner.ReadLanguage("ChineseSimplified")["base"]);
        True(owner.ReadLanguage("English").ContainsKey("owner-added"));
        True(owner.ReadLanguage("English").ContainsKey("product-added"));
        Change(owner, "ChineseSimplified", "base", "再次修改 {0}");
        Equal(0, owner.PreviewCollaboration(returned).Entries.Count);
        product.AddSourceEntries(new() { ["second-return"] = "More product text" });
        product.ExportCollaboration(returned);
        plan = owner.PreviewCollaboration(returned);
        Equal(0, plan.ConflictCount);
        Equal(1, plan.Entries.Count);
        owner.ApplyMerge(plan);
        Equal("再次修改 {0}", owner.ReadLanguage("ChineseSimplified")["base"]);
        True(owner.ReadLanguage("English").ContainsKey("second-return"));
        Equal(0, ProjectWorkspace.Open(owner.FilePath).PreviewCollaboration(returned).Entries.Count);
    }

    private static void LanguageImport()
    {
        var project = CreateProject();
        project.AddSourceEntries(new() { ["keep"] = "Keep" });
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
        var file = SharePath(project, "English.json");
        File.WriteAllText(file, "{\"same\":\"one\",\"same\":\"two\"}");
        Throws<Newtonsoft.Json.JsonReaderException>(() => project.PreviewLanguageImport(new[] { new KeyValuePair<string, string>("English", file) }));
    }

    private static void MergeDeletion()
    {
        var owner = CreateProject();
        var share = SharePath(owner);
        owner.ExportCollaboration(share);
        var product = ProjectWorkspace.Open(share);
        product.DeleteKeys(new[] { "base" });
        Change(owner, "ChineseSimplified", "base", "我的新译文 {0}");
        var plan = owner.PreviewCollaboration(share);
        Equal(1, plan.ConflictCount);
        plan.Entries.Single(e => e.IsConflict).Resolution = MergeResolution.KeepCurrent;
        Throws<InvalidOperationException>(() => owner.ApplyMerge(plan));
        True(owner.ReadLanguage("English").ContainsKey("base"));
        plan.Entries.Single(e => e.Language == "English").Resolution = MergeResolution.KeepCurrent;
        owner.ApplyMerge(plan);
        Equal("我的新译文 {0}", owner.ReadLanguage("ChineseSimplified")["base"]);
        True(owner.ReadLanguage("English").ContainsKey("base"));
        owner = CreateProject();
        share = SharePath(owner);
        owner.ExportCollaboration(share);
        product = ProjectWorkspace.Open(share);
        product.DeleteKeys(new[] { "base" });
        plan = owner.PreviewCollaboration(share);
        Equal(0, plan.ConflictCount);
        owner.ApplyMerge(plan);
        True(owner.Pairs.All(pair => !owner.ReadLanguage(pair.Name).ContainsKey("base")));
    }

    private static void MergeDraft()
    {
        var owner = CreateProject();
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
        var plan = Import(owner, new() { ["new"] = "New" });
        owner.AddSourceEntries(new() { ["meanwhile"] = "Other work" });
        Throws<InvalidOperationException>(() => owner.ApplyMerge(plan));
        True(!owner.ReadLanguage("English").ContainsKey("new"));
        owner.ApplyMerge(owner.RefreshMergePlan(plan));
        owner = ProjectWorkspace.Open(owner.FilePath);
        True(owner.HasMergeBackup);
        owner.UndoLastMerge();
        True(!owner.ReadLanguage("English").ContainsKey("new"));
        True(owner.ReadLanguage("English").ContainsKey("meanwhile"));
        owner.ApplyMerge(Import(owner, new() { ["new"] = "New" }));
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

    private static void InvalidCollaboration()
    {
        var owner = CreateProject();
        var unrelated = CreateProject();
        var file = SharePath(owner);
        unrelated.ExportCollaboration(file);
        Throws<InvalidDataException>(() => owner.PreviewCollaboration(file));
        owner.ExportCollaboration(file);
        var json = JObject.Parse(File.ReadAllText(file));
        json["CollaborationBase"]![0]!["Name"] = "Unknown";
        File.WriteAllText(file, json.ToString());
        Throws<InvalidDataException>(() => owner.PreviewCollaboration(file));
        owner.ExportCollaboration(file);
        json = JObject.Parse(File.ReadAllText(file));
        json["LastMergeBackup"] = "../other.locproj";
        File.WriteAllText(file, json.ToString());
        Throws<InvalidDataException>(() => ProjectWorkspace.Open(file));
    }
}