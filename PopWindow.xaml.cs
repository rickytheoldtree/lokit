using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using LocalizationTranslationWPF.Utils;

namespace LocalizationTranslationWPF;

public partial class PopWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Action<PopWindow> onCancel;
    private readonly DateTime startedAt = DateTime.Now;
    private readonly DispatcherTimer elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool cancellationRequested;
    private bool allowClose;

    public PopWindow(string title, string btnText, Action<PopWindow> onCancel)
    {
        InitializeComponent();
        this.onCancel = onCancel;
        TextBlock.Text = title;
        Btn.Content = btnText;
        elapsedTimer.Tick += ElapsedTimerTick;
        LogUtils.EntryAdded += LogEntryAdded;
        Loaded += PopWindowLoaded;
        Closed += PopWindowClosed;
    }

    public void CloseFromOwner()
    {
        allowClose = true;
        Close();
    }

    private void PopWindowLoaded(object sender, RoutedEventArgs e)
    {
        ThemeUtils.Attach(this, ConfigUtils.AppTheme.Value);
        elapsedTimer.Start();
        UpdateElapsedTime();
    }

    private void PopWindowClosed(object? sender, EventArgs e)
    {
        elapsedTimer.Stop();
        elapsedTimer.Tick -= ElapsedTimerTick;
        LogUtils.EntryAdded -= LogEntryAdded;
    }

    private void ElapsedTimerTick(object? sender, EventArgs e) => UpdateElapsedTime();

    private void UpdateElapsedTime()
    {
        var elapsed = DateTime.Now - startedAt;
        ElapsedTextBlock.Text = elapsed.TotalHours >= 1
            ? $"已运行 {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"已运行 {elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void LogEntryAdded(LogEntry entry)
    {
        LatestStepTextBlock.Text = entry.Text;
        LatestStepTextBlock.ToolTip = entry.Text;
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e) => RequestCancellation();

    private void RequestCancellation()
    {
        if (cancellationRequested) return;
        cancellationRequested = true;
        TextBlock.Text = "正在安全停止任务，请稍候…";
        LatestStepTextBlock.Text = "等待当前请求结束";
        Btn.IsEnabled = false;
        onCancel(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            RequestCancellation();
        }
        else
        {
            ThemeUtils.Detach(this);
        }
        base.OnClosing(e);
    }
}
