using System;
using System.Windows;

namespace LocalizationTranslationWPF;

public partial class PopWindow : Window
{
    private readonly Action<PopWindow> onCancel;

    public PopWindow(string title, string btnText, Action<PopWindow> onCancel)
    {
        InitializeComponent();
        this.onCancel = onCancel;
        TextBlock.Text = title;
        Btn.Content = btnText;
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        onCancel.Invoke(this);
    }
}
