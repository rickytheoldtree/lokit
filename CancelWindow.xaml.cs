using System;
using System.Windows;

namespace LocalizationTranslationWPF;

public partial class CancelWindow : Window
{
    // 提供一个事件，用于通知外部点击了取消按钮
    public event EventHandler? CancelRequested;
    
    public CancelWindow()
    {
        InitializeComponent();
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}