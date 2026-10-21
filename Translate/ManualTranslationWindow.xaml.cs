using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lokit.Utils;
using Wpf.Ui.Controls;

namespace Lokit.Translate;

public partial class ManualTranslationWindow : FluentWindow
{
    private readonly Func<string, Dictionary<string, string>> validateResponse;
    private readonly TaskCompletionSource<Dictionary<string, string>?> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationToken requestToken;

    public ManualTranslationWindow(string description, string prompt,
        Func<string, Dictionary<string, string>> validateResponse)
    {
        InitializeComponent();
        this.validateResponse = validateResponse;
        LanguageSummaryTextBlock.Text = description;
        PromptTextBox.Text = prompt;
        Loaded += (_, _) => ThemeUtils.Attach(this, ConfigUtils.AppTheme.Value);
        Closing += (_, _) => ThemeUtils.Detach(this);
        Closed += (_, _) => completion.TrySetCanceled();
    }

    public async Task<Dictionary<string, string>?> ShowAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        requestToken = token;
        using var registration = token.Register(() =>
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                completion.TrySetCanceled(token);
                Close();
            });
        });
        try
        {
            Show();
            return await completion.Task;
        }
        finally
        {
            if (IsVisible) Close();
        }
    }

    private void CopyPromptClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PromptTextBox.Text);
            ShowMessage("提示词已复制", "粘贴到 AI 对话中，翻译完成后复制完整回复。", InfoBarSeverity.Success);
        }
        catch (Exception)
        {
            ShowMessage("暂时无法复制", "剪贴板正忙，请重试，或选中提示词后按 Ctrl+C。", InfoBarSeverity.Warning);
        }
    }

    private void PasteResponseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowMessage("剪贴板中没有文本", "请先复制 AI 的完整回复。", InfoBarSeverity.Warning);
                return;
            }
            ResponseTextBox.Text = text;
            ResponseTextBox.Focus();
        }
        catch (Exception)
        {
            ShowMessage("暂时无法粘贴", "剪贴板正忙，请重试，或在回复框中按 Ctrl+V。", InfoBarSeverity.Warning);
        }
    }

    private void ResponseTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SubmitResponseButton == null || ResponseInfoBar == null) return;
        SubmitResponseButton.IsEnabled = !string.IsNullOrWhiteSpace(ResponseTextBox.Text);
        ResponseInfoBar.IsOpen = false;
    }

    private void SubmitResponseClick(object sender, RoutedEventArgs e)
    {
        if (requestToken.IsCancellationRequested || completion.Task.IsCompleted) return;
        try
        {
            var result = validateResponse(ResponseTextBox.Text);
            completion.TrySetResult(result);
            Close();
        }
        catch (InvalidDataException error)
        {
            ShowMessage("回复尚未通过校验", error.Message, InfoBarSeverity.Error);
        }
    }

    private void CancelTranslationClick(object sender, RoutedEventArgs e) => Close();

    private void ShowMessage(string title, string message, InfoBarSeverity severity)
    {
        ResponseInfoBar.Title = title;
        ResponseInfoBar.Message = message;
        ResponseInfoBar.Severity = severity;
        ResponseInfoBar.IsOpen = true;
    }
}
