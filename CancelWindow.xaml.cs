using System;
using System.Threading;
using System.Windows;

namespace LocalizationTranslationWPF;

public partial class CancelWindow : Window
{
    private event Action CancelRequested;
    
    public CancelWindow(CancellationTokenSource cts)
    {
        CancelRequested = cts.Cancel;
        InitializeComponent();
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        CancelRequested.Invoke();
        BtnCancelTask.IsEnabled = false;
        BtnCancelTask.Content = "正在取消...";
    }
}