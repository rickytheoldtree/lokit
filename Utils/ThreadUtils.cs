namespace Lokit.Utils
{
    public static class ThreadUtils
    {
        /// <summary>
        /// 读取剪贴板文本；剪贴板必须在STA线程访问，被其它进程占用时返回空串
        /// </summary>
        public static string GetClipboard()
        {
            var result = "";
            var thread = new Thread(() =>
            {
                try
                {
                    result = System.Windows.Clipboard.GetText();
                }
                catch (Exception)
                {
                    result = "";
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }
    }
}
