using System;
using System.Windows;

namespace LocalizationTranslationWPF;

public partial class PopWindow : Window
{
    private event Action<PopWindow> CancelRequested;
    
    public PopWindow(string title, string btnText, Action<PopWindow> onClick)
    {
        InitializeComponent();
        CancelRequested = onClick;
        TextBlock.Text = title;
        Btn.Content = btnText;
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        CancelRequested.Invoke(this);
    }
}