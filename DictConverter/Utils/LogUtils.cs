using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LocalizationTranslationWPF.Utils
{
    public class LogUtils
    {
        private static void UseLogBox(Action<RichTextBox> action)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var logBox = Application.Current.MainWindow?.FindName("logBox") as RichTextBox;
                if (logBox != null)
                {
                    action(logBox);
                }
            });
        }

        public static void Log(string text)
        {
            UseLogBox(logBox =>
            {
                // 创建一个新的文本范围
                var paragraph = new Paragraph();

                // 创建一个带有绿色颜色的 Run
                var run = new Run($"[{GetTime()}] {text}")
                {
                    Foreground = Brushes.Green
                };

                // 将 Run 添加到段落中
                paragraph.Inlines.Add(run);

                // 将段落添加到 RichTextBox
                logBox.Document.Blocks.Add(paragraph);

                // 滚动到最底部
                logBox.ScrollToEnd();
            });
        }

        public static void LogError(string text)
        {
            UseLogBox(logBox =>
            {
                // 创建一个新的文本范围
                var paragraph = new Paragraph();

                // 创建一个带有红色颜色的 Run
                var run = new Run($"[{GetTime()}] {text}")
                {
                    Foreground = Brushes.Red
                };

                // 将 Run 添加到段落中
                paragraph.Inlines.Add(run);

                // 将段落添加到 RichTextBox
                logBox.Document.Blocks.Add(paragraph);

                // 滚动到最底部
                logBox.ScrollToEnd();
            });
        }

        private static string GetTime()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
