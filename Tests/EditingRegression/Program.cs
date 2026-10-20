using System.Data;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DictConverter;
using LocalizationTranslationWPF;
using LocalizationTranslationWPF.Data;
using LocalizationTranslationWPF.Editing;
using LocalizationTranslationWPF.Navigation;
using LocalizationTranslationWPF.Projects;
using LocalizationTranslationWPF.Translate;
using LocalizationTranslationWPF.Utils;

internal static partial class Program
{
    private static readonly DefaultConverter Converter = new();
    private static readonly Pair[] Pairs = { new("English", "en"), new("ChineseSimplified", "zh-CN"), new("French", "fr") };
    private static int passed;
    private static int failed;

    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.CurrentDirectory = root;
        PathUtils.MakeSureFolderExist();
        Run("Project creation/open is read-only for official files; only saves workspace", ProjectRoundtrip);
        Run("Add, duplicate validation, sorted insert, search and undo", AddAndValidation);
        Run("Editor save, dirty cancellation, reload and stale snapshot protection", Editor);
        Run("Save drafts feeds translation directly without intermediate files", TranslationPipeline);
        Run("Existing translations are preserved until explicitly marked for retranslation", TranslationSelection);
        Run("Invalid keys/placeholders or obsolete results cannot be applied", InvalidTranslation);
        Run("Cancellation does not apply in-flight results", Cancellation);
        Run("Publish preview, diff, byte-exact backup and restore", PublishRestore);
        Run("External changes and stale/wrong-project previews block publishing", Conflicts);
        Run("Publish state-commit failure rolls back every language file", PublishRollback);
        Run("Restore rejects externally modified published files", RestoreConflict);
        Run("Multiple instances combine separate edits and protect conflicting values", WorkspaceConflict);
        Run("Dropped JSON merges against its export, repeats are no-ops and follow-ups show only new edits", JsonBaseMerge);
        Run("Stale, partial and unrelated JSON never revert or delete newer content", StaleAndPartialFiles);
        Run("JSON import previews conflicts and marks changed sources", LanguageImport);
        Run("Source deletion and modified translations require consistent choices", MergeDeletion);
        Run("Merge choices survive restart and unrelated editing", MergeDraft);
        Run("Stale previews preserve data and merge history can be undone safely", MergeHistory);
        Run("External resource sync rebases working changes and checks concurrent writes", ResourceSync);
        Run("Merge ignores corrupt or foreign version records and rejects tampered metadata", TamperedMergeData);
        Run("Project validation rejects traversal/duplicate names and bad source files", InvalidProjects);
        Run("New and explicitly marked old keys translate together with per-language retry state", ForcedRetranslation);
        Run("Bulk paste batches validation and state notifications", BulkPaste);
        if (args.Contains("--ui")) Run("WPF project, add, edit, translation and publish preview workflow", WpfSmoke);
        ProjectContext.Current = null;
        ConfigUtils.FlushPendingSave();
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        Console.WriteLine($"Isolated test data/screenshots: {root}");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            ProjectContext.Current = null;
            ConfigUtils.LastProject.SetWithoutInvoke("");
            ConfigUtils.DictConverter.SetWithoutInvoke(typeof(DefaultConverter));
            ConfigUtils.AppTheme.SetWithoutInvoke(Config.AppThemeMode.Light);
            test();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error}"); }
    }

    private static ProjectWorkspace CreateProject()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, Guid.NewGuid().ToString("N"));
        var target = Path.Combine(directory, "game-languages");
        Directory.CreateDirectory(target);
        Write(Path.Combine(target, "English.json"), new() { ["base"] = "Hello {0}" });
        Write(Path.Combine(target, "ChineseSimplified.json"), new() { ["base"] = "你好 {0}" });
        var project = ProjectWorkspace.Create(Path.Combine(directory, "Sample.locproj"), target, Pairs, "English", typeof(DefaultConverter));
        ProjectContext.Current = project;
        ConfigUtils.SourceLangIndex.Value = 0;
        ConfigUtils.TargetLangIndex.Value = 1;
        ConfigUtils.TargetLangAmount.Value = Config.TargetLangAmount.Single;
        return project;
    }

    private static void ProjectRoundtrip()
    {
        var project = CreateProject();
        var original = File.ReadAllBytes(Target(project, "English"));
        project.AddSourceEntries(new() { ["new"] = "New" });
        Equal(1, project.PendingChangeCount);
        True(original.SequenceEqual(File.ReadAllBytes(Target(project, "English"))));
        var reopened = ProjectWorkspace.Open(project.FilePath);
        Equal("New", reopened.ReadLanguage("English")["new"]);
        Equal("English", reopened.SourceLanguage.Name);
        Equal(3, reopened.Pairs.Count);
        True(!File.ReadAllText(project.FilePath).Contains("aiAPIKey"));
        Throws<IOException>(() => IOUtils.AtomicWriteAllBytes(project.FilePath, Encoding.UTF8.GetBytes("broken"), overwrite: false));
        Equal("New", ProjectWorkspace.Open(project.FilePath).ReadLanguage("English")["new"]);
        Throws<InvalidDataException>(() => project.AddSourceEntries(new() { ["null-value"] = null! }));
        True(!project.ContainsSourceKey("null-value"));
        True(!Directory.Exists("data/input") && !Directory.Exists("data/output"));
        var legacyJson = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(project.FilePath));
        foreach (var language in (Newtonsoft.Json.Linq.JArray)legacyJson["Languages"]!)
            ((Newtonsoft.Json.Linq.JObject)language).Property("RetranslateKeys")?.Remove();
        File.WriteAllText(project.FilePath, legacyJson.ToString());
        Equal(0, ProjectWorkspace.Open(project.FilePath).PendingRetranslationCount);
    }

    private static void AddAndValidation()
    {
        var project = CreateProject();
        using var session = new AddNewSession();
        session.AppendTabSeparatedText("same\tOne\nsame\tTwo\n \tMissing\nbase\tExists");
        Equal(4, session.ValidationErrorCount);
        session.View[1][0] = "different";
        Equal(2, session.ValidationErrorCount);
        session.DeleteRows(new[] { session.View[2], session.View[3] });
        Equal(0, session.ValidationErrorCount);
        session.View.Sort = "Key ASC";
        session.ApplyFilter("missing");
        var added = session.AddEmptyRow();
        Equal("", (string)added[0]);
        added[0] = "literal";
        added[1] = "'[x] 100% * \\[]";
        foreach (var text in new[] { "'", "[", "]", "[x]", "%", "*", "\\", "[]" })
        {
            session.ApplyFilter(text);
            Equal(1, session.VisibleRowCount);
        }
        True(session.Save());
        Equal(4, project.ReadLanguage("English").Count);
        Equal(0, session.TotalRowCount);
        session.AppendTabSeparatedText("extra\tDraft");
        True(session.UndoChanges());
        Equal(0, session.TotalRowCount);
        True(!session.IsDirty);
    }

    private static void Editor()
    {
        var project = CreateProject();
        var editor = new EditorSession();
        editor.RefreshPairs();
        True(editor.Load(Pairs[0]));
        var row = editor.View![0];
        row.BeginEdit(); row[1] = "Pending";
        True(editor.IsDirty);
        row.CancelEdit(); True(!editor.IsDirty);
        row[1] = "Hello {0}"; True(!editor.IsDirty);
        editor.ApplyFilter("base");
        row[1] = "Changed {0}";
        True(editor.Save());
        Equal("Changed {0}", project.ReadLanguage("English")["base"]);
        Equal("Hello {0}", Read(Target(project, "English"))["base"]);
        row = editor.View![0];
        row[1] = "Local";
        var remote = project.ReadLanguage("English");
        project.SaveLanguage("English", new() { ["base"] = "Remote" }, remote);
        Throws<InvalidOperationException>(() => editor.Save());
        True(editor.IsDirty);
        editor.DiscardChanges();
        True(editor.NeedsReload);
        editor.Load(Pairs[0]);
        var events = 0;
        editor.StateChanged += () => events++;
        row[1] = "Detached edit";
        Equal(0, events);
        editor.DeleteKeys(new[] { "base" });
        Equal(0, project.ReadLanguage("ChineseSimplified").Count);
        Equal(1, Read(Target(project, "English")).Count);
    }

    private static void TranslationPipeline()
    {
        var project = CreateProject();
        True(!Converter.TryDeserialize("我不是 JSON", out _));
        var formattedPrompt = TranslationJob.FormatPrompt(ConfigUtils.DefaultAIPrompt, Pairs[1], "{\"a\":\"One\"}");
        True(formattedPrompt.Contains("{0}") && formattedPrompt.Contains("ChineseSimplified") &&
             formattedPrompt.Contains("{\"a\":\"One\"}") && !formattedPrompt.Contains('\n'));
        var add = new AddNewSession();
        add.AppendTabSeparatedText("new\tNew {0}"); True(add.Save());
        var calls = 0;
        True(TranslationJob.RunAsync(Converter, (_, _, source, _, _) =>
        {
            calls++;
            Equal(1, source.Count);
            True(source.ContainsKey("new"));
            return Task.FromResult<Dictionary<string, string>?>(new() { ["new"] = "新 {0}" });
        }, null, CancellationToken.None).GetAwaiter().GetResult());
        Equal(1, calls);
        Equal("新 {0}", project.ReadLanguage("ChineseSimplified")["new"]);
        True(!Read(Target(project, "ChineseSimplified")).ContainsKey("new"));
        var ai = TranslationJob.TranslateWithAiAsync(new() { ["a"] = "One" }, Pairs[1], Converter,
            (_, _, _) => Task.FromResult("```json\n{\"a\":\"一\"}\n```[end]"), CancellationToken.None).GetAwaiter().GetResult();
        Equal("一", ai!["a"]);
        ai = TranslationJob.TranslateWithAiAsync(new() { ["a"] = "One {0}" }, Pairs[1], Converter,
            (_, _, _) => Task.FromResult("我已经翻译好了。\n{\"note\":\"说明\"}\n{\"a\":\"一 {0}\"}[end]"),
            CancellationToken.None).GetAwaiter().GetResult();
        Equal("一 {0}", ai!["a"]);
        var invalidResponse = Throws<InvalidDataException>(() => TranslationJob.TranslateWithAiAsync(
            new() { ["a"] = "One" }, Pairs[1], Converter,
            (_, _, _) => Task.FromResult("我无法提供要求的结构化内容。"), CancellationToken.None)
            .GetAwaiter().GetResult());
        True(invalidResponse.Message.Contains("响应开头") && invalidResponse.Message.Contains("我无法"));
        ConfigUtils.TargetLangAmount.Value = Config.TargetLangAmount.All;
        True(!TranslationJob.RunAsync(Converter, (_, to, source, _, _) =>
            to.Name == "French" ? throw new IOException("Service failed") : Task.FromResult<Dictionary<string, string>?>(source),
            null, CancellationToken.None).GetAwaiter().GetResult());
        Equal(0, project.ReadLanguage("French").Count);
    }

    private static void TranslationSelection()
    {
        var project = CreateProject();
        var before = project.ReadLanguage("English");
        project.SaveLanguage("English", new() { ["base"] = "Updated {0}" }, before);
        var source = project.ReadLanguage("English");
        Equal(0, project.PrepareTranslation("ChineseSimplified", source).Source.Count);
        project.SetRetranslation(new[] { "base" }, new[] { "ChineseSimplified" }, true);
        Equal(1, project.PrepareTranslation("ChineseSimplified", source).Source.Count);
    }

    private static void InvalidTranslation()
    {
        var project = CreateProject();
        var input = project.PrepareTranslation("French", project.ReadLanguage("English"));
        Throws<InvalidDataException>(() => project.ApplyTranslation(input, new() { ["wrong"] = "Texte" }));
        Throws<InvalidDataException>(() => project.ApplyTranslation(input, new() { ["base"] = "Sans marqueur" }));
        Equal(0, project.ReadLanguage("French").Count);
        var expected = project.ReadLanguage("English");
        project.SaveLanguage("English", new() { ["base"] = "New {0}" }, expected);
        Throws<InvalidOperationException>(() => project.ApplyTranslation(input, new() { ["base"] = "Ancien {0}" }));
        input = project.PrepareTranslation("French", project.ReadLanguage("English"));
        project.SaveLanguage("French", new() { ["base"] = "Manuel {0}" }, project.ReadLanguage("French"));
        Throws<InvalidOperationException>(() => project.ApplyTranslation(input, new() { ["base"] = "Auto {0}" }));
        Equal("Manuel {0}", project.ReadLanguage("French")["base"]);
    }

    private static void Cancellation()
    {
        var project = CreateProject();
        ConfigUtils.TargetLangIndex.Value = 2;
        project.SetRetranslation(new[] { "base" }, new[] { "French" }, true);
        using var cts = new CancellationTokenSource();
        Throws<OperationCanceledException>(() => TranslationJob.RunAsync(Converter, (_, _, source, _, _) =>
        {
            cts.Cancel();
            return Task.FromResult<Dictionary<string, string>?>(source);
        }, null, cts.Token).GetAwaiter().GetResult());
        Equal(0, project.ReadLanguage("French").Count);
        Equal(1, project.PendingRetranslationCount);
    }

    private static void PublishRestore()
    {
        var project = CreateProject();
        var path = Target(project, "English");
        var exactBytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("{\r\n  \"base\": \"Hello {0}\"\r\n}\r\n")).ToArray();
        // Recreate from a UTF-16 source so restoration must preserve encoding and whitespace.
        File.WriteAllBytes(path, exactBytes);
        project = ProjectWorkspace.Create(Path.Combine(Path.GetDirectoryName(project.FilePath)!, "Encoded.locproj"), project.TargetDirectory, Pairs, "English", typeof(DefaultConverter));
        project.AddSourceEntries(new() { ["new"] = "New" });
        project.SaveLanguage("French", new() { ["new"] = "Nouveau" }, new());
        var plan = project.PreviewPublish();
        Equal(2, plan.Files.Count);
        True(plan.Changes.All(c => c.Kind == "新增"));
        var backup = project.Publish(plan);
        True(File.Exists(backup));
        True(File.Exists(Target(project, "French")));
        Equal(0, project.PendingChangeCount);
        project = ProjectWorkspace.Open(project.FilePath);
        project.RestoreLastPublish();
        True(exactBytes.SequenceEqual(File.ReadAllBytes(path)));
        True(!File.Exists(Target(project, "French")));
        True(project.PendingChangeCount > 0);
        True(project.ReadLanguage("English").ContainsKey("new"));
    }

    private static void Conflicts()
    {
        var project = CreateProject();
        project.AddSourceEntries(new() { ["new"] = "New" });
        var plan = project.PreviewPublish();
        project.AddSourceEntries(new() { ["another"] = "Other" });
        Throws<InvalidOperationException>(() => project.Publish(plan));
        var other = CreateProject();
        other.AddSourceEntries(new() { ["a"] = "A" });
        Throws<InvalidOperationException>(() => other.Publish(plan));
        File.AppendAllText(Target(project, "English"), " ");
        var conflict = project.PreviewPublish();
        True(!conflict.CanPublish && conflict.Conflicts.Count > 0);
        Throws<InvalidOperationException>(() => project.Publish(conflict));
        True(!Read(Target(project, "English")).ContainsKey("new"));
    }

    private static void PublishRollback()
    {
        var project = CreateProject();
        var original = File.ReadAllBytes(Target(project, "English"));
        project.AddSourceEntries(new() { ["new"] = "New" });
        project.SaveLanguage("French", new() { ["new"] = "Nouveau" }, new());
        var plan = project.PreviewPublish();
        File.SetAttributes(project.FilePath, FileAttributes.ReadOnly);
        try { Throws<IOException>(() => project.Publish(plan)); }
        finally { File.SetAttributes(project.FilePath, FileAttributes.Normal); }
        True(original.SequenceEqual(File.ReadAllBytes(Target(project, "English"))));
        True(!File.Exists(Target(project, "French")));
        True(project.PendingChangeCount > 0);
        True(Directory.EnumerateFiles(project.BackupDirectory).Any());
    }

    private static void RestoreConflict()
    {
        var project = CreateProject();
        project.AddSourceEntries(new() { ["new"] = "New" });
        project.Publish(project.PreviewPublish());
        File.AppendAllText(Target(project, "English"), "\n");
        var bytes = File.ReadAllBytes(Target(project, "English"));
        Throws<IOException>(() => project.RestoreLastPublish());
        True(bytes.SequenceEqual(File.ReadAllBytes(Target(project, "English"))));
    }

    private static void WorkspaceConflict()
    {
        var first = CreateProject();
        var second = ProjectWorkspace.Open(first.FilePath);
        first.AddSourceEntries(new() { ["first"] = "One" });
        second.AddSourceEntries(new() { ["second"] = "Two" });
        var combined = ProjectWorkspace.Open(first.FilePath).ReadLanguage("English");
        True(combined.ContainsKey("first") && combined.ContainsKey("second"));
        var expected = first.ReadLanguage("English");
        var otherExpected = second.ReadLanguage("English");
        first.SaveLanguage("English", new(expected) { ["base"] = "First" }, expected);
        Throws<EditorMergeConflictException>(() => second.SaveLanguage("English", new(otherExpected) { ["base"] = "Second" }, otherExpected));
        Equal("First", ProjectWorkspace.Open(first.FilePath).ReadLanguage("English")["base"]);
    }

    private static void InvalidProjects()
    {
        var project = CreateProject();
        var path = Path.Combine(Path.GetDirectoryName(project.FilePath)!, "bad.locproj");
        Throws<InvalidDataException>(() => ProjectWorkspace.Create(path, project.TargetDirectory,
            new[] { new Pair("../escape", "en") }, "../escape", typeof(DefaultConverter)));
        True(!File.Exists(path));
        Throws<InvalidDataException>(() => ProjectWorkspace.Create(path, project.TargetDirectory,
            new[] { new Pair("English", "en"), new Pair("english", "fr") }, "English", typeof(DefaultConverter)));
        File.WriteAllText(Target(project, "French"), "not JSON");
        Throws<InvalidDataException>(() => ProjectWorkspace.Create(path, project.TargetDirectory, Pairs, "English", typeof(DefaultConverter)));
        True(!File.Exists(path));
    }

    private static void ForcedRetranslation()
    {
        var project = CreateProject();
        project.SaveLanguage("French", new() { ["base"] = "Bonjour {0}" }, new());
        project.AddSourceEntries(new() { ["new"] = "New {1}" });
        project.SetRetranslation(new[] { "base" }, new[] { "ChineseSimplified", "French" }, true);
        Equal(2, project.PendingRetranslationCount);
        Throws<InvalidOperationException>(() => project.SetRetranslation(new[] { "base" }, new[] { "English" }, true));
        Throws<InvalidDataException>(() => project.SetRetranslation(new[] { "missing" }, new[] { "French" }, true));

        project = ProjectWorkspace.Open(project.FilePath);
        ProjectContext.Current = project;
        Equal(2, project.PendingRetranslationCount);
        var chineseInput = project.PrepareTranslation("ChineseSimplified", project.ReadLanguage("English"));
        True(chineseInput.Source.Keys.ToHashSet().SetEquals(new[] { "base", "new" }));
        Throws<InvalidDataException>(() => project.ApplyTranslation(chineseInput, new() { ["base"] = "错误 {0}" }));
        Equal("你好 {0}", project.ReadLanguage("ChineseSimplified")["base"]);
        Equal(2, project.PendingRetranslationCount);

        ConfigUtils.TargetLangIndex.Value = 1;
        ConfigUtils.TargetLangAmount.Value = Config.TargetLangAmount.Single;
        var chineseCalls = 0;
        True(TranslationJob.RunAsync(Converter, (_, to, source, _, _) =>
        {
            chineseCalls++;
            Equal("ChineseSimplified", to.Name);
            True(source.Keys.ToHashSet().SetEquals(new[] { "base", "new" }));
            return Task.FromResult<Dictionary<string, string>?>(new()
            {
                ["base"] = "重译 {0}",
                ["new"] = "新增 {1}",
            });
        }, null, CancellationToken.None).GetAwaiter().GetResult());
        Equal(1, chineseCalls);
        Equal("重译 {0}", project.ReadLanguage("ChineseSimplified")["base"]);
        Equal(1, project.PendingRetranslationCount);

        project.SetRetranslation(new[] { "base" }, new[] { "French" }, false);
        Equal(0, project.PendingRetranslationCount);
        True(project.PrepareTranslation("French", project.ReadLanguage("English")).Source.Keys
            .ToHashSet().SetEquals(new[] { "new" }));
        project.SetRetranslation(new[] { "base" }, new[] { "French" }, true);
        ConfigUtils.TargetLangAmount.Value = Config.TargetLangAmount.All;
        var frenchCalls = 0;
        True(TranslationJob.RunAsync(Converter, (_, to, source, _, _) =>
        {
            frenchCalls++;
            Equal("French", to.Name);
            True(source.Keys.ToHashSet().SetEquals(new[] { "base", "new" }));
            return Task.FromResult<Dictionary<string, string>?>(new()
            {
                ["base"] = "Corrigé {0}",
                ["new"] = "Nouveau {1}",
            });
        }, null, CancellationToken.None).GetAwaiter().GetResult());
        Equal(1, frenchCalls);
        Equal(0, project.PendingRetranslationCount);
        Equal("Corrigé {0}", project.ReadLanguage("French")["base"]);
        True(!Read(Target(project, "ChineseSimplified")).ContainsKey("new"));
    }

    private static void BulkPaste()
    {
        CreateProject();
        using var session = new AddNewSession();
        var events = 0;
        session.StateChanged += () => events++;
        Equal(3000, session.AppendTabSeparatedText(string.Join('\n', Enumerable.Range(0, 3000).Select(i => $"key-{i}\tText"))));
        True(events <= 2);
        session.AppendTabSeparatedText("tabs\tText\twith tab");
        Equal("Text\twith tab", (string)session.View[3000][1]);
        events = 0;
        session.ApplyFilter("key-1");
        Equal(1, events);
        events = 0;
        session.Reset();
        Equal(1, events);
    }

    private static void WpfSmoke()
    {
        var project = CreateProject();
        ProjectContext.Current = null;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Light });
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/LocalizationTranslationWPF;component/Themes/AppStyles.xaml") });
        MainWindow? window = null;
        try
        {
            // Reproduce a real cold start: App.xaml begins with its Light fallback,
            // while the persisted preference is already Dark before the window exists.
            ConfigUtils.AppTheme.SetWithoutInvoke(Config.AppThemeMode.Dark);
            window = new MainWindow { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            window.Show(); Pump();
            var navigation = Control<Wpf.Ui.Controls.NavigationView>(window, "RootNavigationView");
            Equal(typeof(TranslationPage), navigation.SelectedItem?.TargetPageType);
            Equal(180d, navigation.OpenPaneLength);
            Equal(new Thickness(0), navigation.FrameMargin);
            True(navigation.IsPaneOpen);
            Equal(new Thickness(20, 20, 20, 0), Control<FrameworkElement>(window, "ProjectToolbar").Margin);
            True(Control<Wpf.Ui.Controls.TitleBar>(window, "MainTitleBar").Icon != null);
            Equal(Wpf.Ui.Appearance.ApplicationTheme.Dark,
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            Equal(2, Control<ComboBox>(window, "ThemeModeComboBox").SelectedIndex);
            Capture(window, "welcome-dark-startup.png");
            var startupTitleBrush = (SolidColorBrush)Control<TextBlock>(window, "TranslationPageTitleTextBlock").Foreground;
            True(startupTitleBrush.Color.R + startupTitleBrush.Color.G + startupTitleBrush.Color.B > 300,
                $"Dark startup title foreground was {startupTitleBrush.Color}.");
            True(!Control<ButtonBase>(window, "BtnStart").IsEnabled);
            True(navigation.Navigate(typeof(AddNewPage))); Pump();
            var addNewEmptyTitleBrush = (SolidColorBrush)Control<TextBlock>(window, "AddNewEmptyStateTitle").Foreground;
            var addNewEmptyDescriptionBrush = (SolidColorBrush)Control<TextBlock>(window, "AddNewEmptyStateDescription").Foreground;
            True(addNewEmptyTitleBrush.Color.R + addNewEmptyTitleBrush.Color.G + addNewEmptyTitleBrush.Color.B > 300,
                $"Dark startup add-new empty title foreground was {addNewEmptyTitleBrush.Color}.");
            True(addNewEmptyDescriptionBrush.Color.R + addNewEmptyDescriptionBrush.Color.G + addNewEmptyDescriptionBrush.Color.B > 300,
                $"Dark startup add-new empty description foreground was {addNewEmptyDescriptionBrush.Color}.");
            Capture(window, "add-new-dark-startup.png");
            Invoke(window, "SetProject", project); Pump();
            Click(window, "BtnAddNewRow"); Pump();
            var darkAddGrid = Control<DataGrid>(window, "DataGridAddNew");
            var darkAddRow = darkAddGrid.CurrentItem;
            darkAddGrid.CurrentCell = new DataGridCellInfo(darkAddRow, darkAddGrid.Columns[0]);
            True(darkAddGrid.BeginEdit()); Pump();
            var darkKeyEditor = (TextBox)darkAddGrid.Columns[0].GetCellContent(darkAddRow);
            var darkKeyEditorForeground = (SolidColorBrush)darkKeyEditor.Foreground;
            True(darkKeyEditorForeground.Color.R + darkKeyEditorForeground.Color.G + darkKeyEditorForeground.Color.B > 300,
                $"Dark add-new key editor foreground was {darkKeyEditorForeground.Color}.");
            Equal(byte.MaxValue, ((SolidColorBrush)darkKeyEditor.Background).Color.A);
            darkKeyEditor.Text = "theme-key";
            True(darkAddGrid.CommitEdit(DataGridEditingUnit.Cell, true)); Pump();
            darkAddGrid.CurrentCell = new DataGridCellInfo(darkAddRow, darkAddGrid.Columns[1]);
            True(darkAddGrid.BeginEdit()); Pump();
            var darkValueEditor = (TextBox)darkAddGrid.Columns[1].GetCellContent(darkAddRow);
            var darkValueEditorForeground = (SolidColorBrush)darkValueEditor.Foreground;
            True(darkValueEditorForeground.Color.R + darkValueEditorForeground.Color.G + darkValueEditorForeground.Color.B > 300,
                $"Dark add-new value editor foreground was {darkValueEditorForeground.Color}.");
            Equal(byte.MaxValue, ((SolidColorBrush)darkValueEditor.Background).Color.A);
            var darkKeyDisplay = (TextBlock)darkAddGrid.Columns[0].GetCellContent(darkAddRow);
            var darkKeyDisplayForeground = (SolidColorBrush)darkKeyDisplay.Foreground;
            True(darkKeyDisplayForeground.Color.R + darkKeyDisplayForeground.Color.G + darkKeyDisplayForeground.Color.B > 300,
                $"Dark add-new key display foreground was {darkKeyDisplayForeground.Color}.");
            var darkVisualRow = (DataGridRow)darkAddGrid.ItemContainerGenerator.ContainerFromItem(darkAddRow);
            var darkSelectedCell = Descendants(darkVisualRow).OfType<DataGridCell>()
                .Single(cell => cell.Column.DisplayIndex == 0);
            var darkSelectedCellBackground = ((SolidColorBrush)darkSelectedCell.Background).Color;
            True(darkSelectedCellBackground.A < 128 ||
                 darkSelectedCellBackground.R + darkSelectedCellBackground.G + darkSelectedCellBackground.B < 450,
                $"Dark selected DataGrid cell background was {darkSelectedCellBackground}.");
            Capture(window, "add-new-key-value-edit-dark-startup.png");
            darkAddGrid.CancelEdit(DataGridEditingUnit.Cell);
            darkAddGrid.CancelEdit(DataGridEditingUnit.Row);
            Invoke(window, "SetProject", null); Pump();
            True(navigation.Navigate(typeof(TranslationPage))); PumpDelay(300);
            Equal(new Thickness(0), Control<Wpf.Ui.Controls.Button>(window, "BtnCopyLog").BorderThickness);
            Equal(new Thickness(0), Control<Wpf.Ui.Controls.Button>(window, "BtnClearLog").BorderThickness);
            Equal(new Thickness(0), Control<Wpf.Ui.Controls.Button>(window, "BtnToggleLog").BorderThickness);
            Equal(new Thickness(0), Control<ToggleButton>(window, "ErrorsOnlyToggleButton").BorderThickness);
            Equal(new Thickness(0), Control<ToggleButton>(window, "AutoScrollToggleButton").BorderThickness);
            LogUtils.Clear();
            Control<RichTextBox>(window, "LogBox").Document.Blocks.Clear();
            Click(window, "BtnCopyLog"); PumpDelay(300);
            var emptyLogInfoBar = Control<Wpf.Ui.Controls.InfoBar>(window, "MainInfoBar");
            True(emptyLogInfoBar.IsOpen);
            var emptyLogInfoBarSurface = Control<Border>(window, "MainInfoBarSurface");
            Equal(Visibility.Visible, emptyLogInfoBarSurface.Visibility);
            Equal(byte.MaxValue, ((SolidColorBrush)emptyLogInfoBarSurface.Background).Color.A);
            Capture(window, "empty-log-info-bar-dark.png");
            emptyLogInfoBar.IsOpen = false; Pump();
            typeof(MainWindow).GetMethod("ShowSnackbar", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window,
                new object[] { "任务已取消", "翻译任务已停止。", Wpf.Ui.Controls.ControlAppearance.Caution, Wpf.Ui.Controls.SymbolRegular.Dismiss24 });
            PumpDelay(300);
            var cancellationSnackbar = Descendants(window).OfType<Wpf.Ui.Controls.Snackbar>().Single(item => item.IsShown);
            True(!cancellationSnackbar.IsCloseButtonEnabled);
            Capture(window, "status-tools-cancel-snackbar-dark.png");
            Control<ComboBox>(window, "ThemeModeComboBox").SelectedIndex = 1; Pump();
            Equal(Wpf.Ui.Appearance.ApplicationTheme.Light,
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            Capture(window, "welcome.png");
            True(navigation.Navigate(typeof(SettingsPage))); Pump();
            Equal(typeof(SettingsPage), navigation.SelectedItem?.TargetPageType);
            True(navigation.Navigate(typeof(TranslationPage))); Pump();
            Invoke(window, "SetProject", project); Pump();
            Equal(Visibility.Collapsed, Control<FrameworkElement>(window, "ProjectWelcomeCard").Visibility);
            PumpDelay(300);
            Capture(window, "project.png");
            var cancelInvoked = false;
            var taskWindow = new PopWindow("任务正在运行，请稍候…", "取消任务", _ => cancelInvoked = true)
            {
                Owner = window,
            };
            taskWindow.Show(); Pump();
            True(taskWindow.FindName("TaskAppLogo") is FrameworkElement { ActualWidth: > 0 });
            LogUtils.Log("正在翻译 German：2 条。"); Pump();
            Equal("正在翻译 German：2 条。", ((TextBlock)taskWindow.FindName("LatestStepTextBlock")).Text);
            Capture(taskWindow, "task-light.png");
            Control<ComboBox>(window, "ThemeModeComboBox").SelectedIndex = 2; Pump();
            Equal(Wpf.Ui.Appearance.ApplicationTheme.Dark,
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            Capture(taskWindow, "task-dark.png");
            Capture(window, "project-dark.png");
            ((ButtonBase)taskWindow.FindName("Btn")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            True(cancelInvoked && !((ButtonBase)taskWindow.FindName("Btn")).IsEnabled);
            taskWindow.CloseFromOwner(); Pump();
            Control<ComboBox>(window, "ThemeModeComboBox").SelectedIndex = 1; Pump();
            Equal(Wpf.Ui.Appearance.ApplicationTheme.Light,
                Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme());
            Control<ComboBox>(window, "TargetLangComboBox").SelectedIndex = 0;
            True(!Control<ButtonBase>(window, "BtnStart").IsEnabled);
            Control<ComboBox>(window, "TargetLangAmountComboBox").SelectedIndex = 1;
            True(Control<ButtonBase>(window, "BtnStart").IsEnabled);
            Control<ComboBox>(window, "TargetLangComboBox").SelectedIndex = 1;
            Control<ComboBox>(window, "TargetLangAmountComboBox").SelectedIndex = 0;
            // Translation completion must re-enable publish after Changed ran while the UI was locked.
            var ctsField = typeof(MainWindow).GetField("cts", BindingFlags.NonPublic | BindingFlags.Instance)!;
            using (var tokenSource = new CancellationTokenSource())
            {
                ctsField.SetValue(window, tokenSource);
                Invoke(window, "SetUILock", true);
                Invoke(window, "UpdateProjectUi", null, false);
                True(!Control<ButtonBase>(window, "BtnPublishProject").IsEnabled);
                ctsField.SetValue(window, null);
                Invoke(window, "SetUILock", false);
                True(Control<ButtonBase>(window, "BtnPublishProject").IsEnabled);
            }
            True(navigation.Navigate(typeof(AddNewPage))); Pump();
            Equal(typeof(AddNewPage), navigation.SelectedItem?.TargetPageType);
            Equal(20d, Control<FrameworkElement>(window, "AddNewPageLayout").Margin.Left);
            Click(window, "BtnAddNewRow"); Pump();
            var grid = Control<DataGrid>(window, "DataGridAddNew");
            var session = Session<AddNewSession>(window, "addNewSession");
            var row = (DataRowView)grid.CurrentItem;
            row[0] = "duplicate"; row[1] = "One";
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            session.AppendTabSeparatedText("duplicate\tTwo"); Pump();
            Equal(2, session.ValidationErrorCount);
            True(!navigation.Navigate(typeof(EditorPage))); Pump();
            var unsaved = Descendants(window).OfType<Wpf.Ui.Controls.ContentDialog>()
                .Single(d => d.Title?.ToString() == "新增页有未保存的更改");
            Equal(typeof(AddNewPage), navigation.SelectedItem?.TargetPageType);
            InvokeButton(Descendants(unsaved).OfType<Button>().Single(b => b.Content?.ToString() == "取消"));
            PumpUntil(() => !unsaved.IsVisible);
            Equal(typeof(AddNewPage), navigation.SelectedItem?.TargetPageType);
            var visualRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            True(visualRow.ToolTip?.ToString()?.Contains("重复") == true);
            grid.CurrentCell = new DataGridCellInfo(grid.Items[1], grid.Columns[0]);
            True(grid.BeginEdit()); Pump();
            ((TextBox)grid.Columns[0].GetCellContent(grid.Items[1])).Text = "fixed";
            True(grid.CommitEdit(DataGridEditingUnit.Cell, true)); True(grid.CommitEdit(DataGridEditingUnit.Row, true)); Pump();
            Equal(0, session.ValidationErrorCount);
            True(visualRow.ToolTip == null);
            Capture(window, "add.png");
            Click(window, "BtnAddNewSave"); Pump();
            Equal(0, session.TotalRowCount);
            True(project.ReadLanguage("English").ContainsKey("fixed"));
            True(navigation.Navigate(typeof(EditorPage))); Pump();
            Equal(20d, Control<FrameworkElement>(window, "EditorPageLayout").Margin.Left);
            var editorGrid = Control<DataGrid>(window, "DataGridEditor");
            Equal(3, editorGrid.Items.Count);
            var editor = Session<EditorSession>(window, "editorSession");
            editor.DeleteKeys(new[] { "duplicate" }); Pump();
            True(ReferenceEquals(editor.View, editorGrid.ItemsSource));
            Equal(2, editorGrid.Items.Count);
            editorGrid.SelectedItem = editor.View!.Cast<DataRowView>().Single(row => row[0]?.ToString() == "base");
            Click(window, "BtnEditorRetranslate"); Pump();
            var retranslate = Descendants(window).OfType<Wpf.Ui.Controls.ContentDialog>()
                .Single(d => d.Title?.ToString() == "重新翻译选中的 Key");
            InvokeButton(Descendants(retranslate).OfType<Button>().Single(b => b.Content?.ToString() == "标记重译"));
            PumpUntil(() => project.PendingRetranslationCount == 2);
            Equal(Visibility.Visible, Control<FrameworkElement>(window, "EditorRetranslateTextBlock").Visibility);
            Click(window, "BtnEditorRetranslate"); Pump();
            retranslate = Descendants(window).OfType<Wpf.Ui.Controls.ContentDialog>()
                .Single(d => d.Title?.ToString() == "重新翻译选中的 Key");
            InvokeButton(Descendants(retranslate).OfType<Button>().Single(b => b.Content?.ToString() == "取消标记"));
            PumpUntil(() => project.PendingRetranslationCount == 0);
            window.Width = 900; window.Height = 640; Pump();
            True(!navigation.IsPaneOpen);
            PumpDelay(400);
            Capture(window, "editor-minimum.png");
            Click(window, "BtnPublishProject"); Pump();
            var publish = Descendants(window).OfType<Wpf.Ui.Controls.ContentDialog>().Single(d => d.Title?.ToString() == "发布预览");
            True(publish.IsVisible);
            Capture(window, "publish-preview.png");
            var publishButton = Descendants(publish).OfType<ButtonBase>().Single(b => b.Content?.ToString() == "备份并发布");
            // Invoke the real button action, including its bound command; raising Click alone skips commands.
            InvokeButton((Button)publishButton);
            PumpUntil(() => project.PendingChangeCount == 0);
            Equal(0, project.PendingChangeCount);
            True(Read(Target(project, "English")).ContainsKey("fixed"));
            WpfJsonMerge(window, project);
            WpfManualTranslation(window);
            // Switching projects inside a project action must not leave the project menu disabled afterwards.
            typeof(MainWindow).GetMethod("BtnCloseProjectClick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, new object?[] { null, new RoutedEventArgs() });
            PumpUntil(() => ProjectContext.Current == null);
            True(Control<ButtonBase>(window, "BtnProjectActions").IsEnabled);
            True(!Control<ButtonBase>(window, "BtnAddNewRow").IsEnabled);
        }
        finally
        {
            if (window != null)
            {
                Session<EditorSession>(window, "editorSession").DiscardChanges();
                Session<AddNewSession>(window, "addNewSession").DiscardChanges();
                window.Close();
            }
            app.Shutdown();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void PumpUntil(Func<bool> completed)
    {
        if (completed()) return;
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (completed() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        True(completed(), "Timed out waiting for the WPF operation to complete");
    }
    private static void PumpDelay(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(milliseconds),
        };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }
    private static void Capture(Window window, string filename)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(backdrop); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(filename); encoder.Save(stream);
    }
    private static void InvokeButton(Button button)
    {
        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(
            System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
    }
    private static string Target(ProjectWorkspace project, string language) => Path.Combine(project.TargetDirectory, language + ".json");
    private static void Write(string path, Dictionary<string, string> values) => IOUtils.OutputJson(path, values, Converter);
    private static Dictionary<string, string> Read(string path) { True(IOUtils.TryInputJson(path, Converter, out var data)); return data!; }
    private static T Control<T>(MainWindow window, string name) => (T)window.FindName(name);
    private static T Session<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, object? argument, bool hasArgument = true) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, hasArgument ? new[] { argument } : null);
    private static void Click(MainWindow window, string name) => Control<ButtonBase>(window, name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void True(bool result, string? message = null) { if (!result) throw new InvalidOperationException(message ?? "Assertion failed"); }
    private static void Equal<T>(T expected, T actual) => True(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}
