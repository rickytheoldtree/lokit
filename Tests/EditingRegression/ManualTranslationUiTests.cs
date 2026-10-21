using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Lokit;
using Lokit.Data;
using Lokit.Projects;
using Lokit.Translate;
using Lokit.Utils;

internal static partial class Program
{
    private static void WpfManualTranslation(MainWindow window)
    {
        var project = CreateProject();
        project.AddSourceEntries(new() { ["manual"] = "Line one\nLine two <b>{0}</b>" });
        project.SetRetranslation(new[] { "base" }, new[] { "French" }, true);
        Invoke(window, "SetProject", project);
        var methods = Control<ComboBox>(window, "TranslateMethodComboBox");
        var previousMethod = methods.SelectedIndex;
        var prompt = Control<Wpf.Ui.Controls.TextBox>(window, "TextBoxPrompt");
        var previousPrompt = prompt.Text;
        try
        {
            methods.SelectedIndex = (int)Config.TranslateMethod.ManualAI;
            Equal(Config.TranslateMethod.ManualAI, ConfigUtils.TranslateMethod.Value);
            Equal(Visibility.Visible, Control<TextBlock>(window, "ManualTranslationHint").Visibility);
            // Manual translation remains available without an API key, model or browser driver.
            True(string.IsNullOrEmpty(ConfigUtils.ApiKey.Value));
            prompt.Text = "目标：{0}\n保留 Key、占位符和标签，翻译：{1}";
            Control<ComboBox>(window, "TargetLangAmountComboBox").SelectedIndex = 1;
            True(Control<ButtonBase>(window, "BtnStart").IsEnabled);
            Click(window, "BtnStart"); Pump();
            var dialog = ManualDialog(window);
            True(!window.OwnedWindows.OfType<PopWindow>().Any());
            True(!Control<ButtonBase>(window, "BtnStart").IsEnabled);
            var generated = ((TextBox)dialog.FindName("PromptTextBox")).Text;
            True(generated.Contains("目标：ChineseSimplified", StringComparison.Ordinal));
            True(generated.Contains("\"manual\"", StringComparison.Ordinal));
            True(!generated.Contains("\"base\"", StringComparison.Ordinal));
            var response = (TextBox)dialog.FindName("ResponseTextBox");
            True(!((ButtonBase)dialog.FindName("SubmitResponseButton")).IsEnabled);
            foreach (var invalid in new[]
                     {
                         "不是 JSON",
                         "{\"wrong\":\"错误\"}",
                         "{\"manual\":\"\"}",
                         "{\"manual\":\"缺少标签和占位符\"}",
                     })
            {
                response.Text = invalid;
                SubmitManualResponse(dialog); Pump();
                True(dialog.IsVisible);
                Equal(invalid, response.Text);
                True(((Wpf.Ui.Controls.InfoBar)dialog.FindName("ResponseInfoBar")).IsOpen);
                True(!project.ReadLanguage("ChineseSimplified").ContainsKey("manual"));
            }
            Capture(dialog, "manual-ai-validation-light.png");
            Control<ComboBox>(window, "ThemeModeComboBox").SelectedIndex = 2; Pump();
            Capture(dialog, "manual-ai-validation-dark.png");
            Control<ComboBox>(window, "ThemeModeComboBox").SelectedIndex = 1; Pump();
            response.Text = "翻译如下：\n```json\n{\"manual\":\"第一行\\n第二行 <b>{0}</b>\"}\n```[end]";
            SubmitManualResponse(dialog);
            PumpUntil(() => project.ReadLanguage("ChineseSimplified").ContainsKey("manual"));
            dialog = ManualDialog(window);
            True(((TextBlock)dialog.FindName("LanguageSummaryTextBlock")).Text.Contains("French", StringComparison.Ordinal));
            Equal("你好 {0}", project.ReadLanguage("ChineseSimplified")["base"]);
            Equal("第一行\n第二行 <b>{0}</b>", project.ReadLanguage("ChineseSimplified")["manual"]);
            Capture(dialog, "manual-ai-french.png");
            ((ButtonBase)dialog.FindName("CancelTranslationButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            PumpUntil(() => Control<ButtonBase>(window, "BtnStart").IsEnabled);
            Equal(0, project.ReadLanguage("French").Count);
            Equal(1, project.PendingRetranslationCount);

            // Token cancellation also closes the pending input and preserves retry state.
            Click(window, "BtnStart"); Pump();
            dialog = ManualDialog(window);
            Session<CancellationTokenSource>(window, "cts").Cancel();
            PumpUntil(() => Control<ButtonBase>(window, "BtnStart").IsEnabled);
            True(!dialog.IsVisible);
            Equal(0, project.ReadLanguage("French").Count);
            Equal(1, project.PendingRetranslationCount);

            // A new run skips the completed language and accepts the remaining reply.
            Click(window, "BtnStart"); Pump();
            dialog = ManualDialog(window);
            True(((TextBlock)dialog.FindName("LanguageSummaryTextBlock")).Text.Contains("French", StringComparison.Ordinal));
            ((TextBox)dialog.FindName("ResponseTextBox")).Text =
                "{\"base\":\"Bonjour {0}\",\"manual\":\"Ligne un\\nLigne deux <b>{0}</b>\"}";
            SubmitManualResponse(dialog);
            PumpUntil(() => Control<ButtonBase>(window, "BtnStart").IsEnabled);
            Equal("Bonjour {0}", project.ReadLanguage("French")["base"]);
            Equal(0, project.PendingRetranslationCount);
            Equal("Ligne un\nLigne deux <b>{0}</b>", ProjectWorkspace.Open(project.FilePath).ReadLanguage("French")["manual"]);
            True(!Read(Target(project, "ChineseSimplified")).ContainsKey("manual"));
            True(!File.Exists(Target(project, "French")));
            True(Control<ButtonBase>(window, "BtnPublishProject").IsEnabled);
            Click(window, "BtnStart"); Pump();
            True(!window.OwnedWindows.OfType<ManualTranslationWindow>().Any());
            True(Control<ButtonBase>(window, "BtnStart").IsEnabled);
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.OfType<ManualTranslationWindow>().ToArray()) dialog.Close();
            Pump();
            prompt.Text = previousPrompt;
            methods.SelectedIndex = previousMethod;
        }
    }

    private static ManualTranslationWindow ManualDialog(MainWindow owner) =>
        owner.OwnedWindows.OfType<ManualTranslationWindow>().Single();

    private static void SubmitManualResponse(ManualTranslationWindow dialog) =>
        ((ButtonBase)dialog.FindName("SubmitResponseButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
}
