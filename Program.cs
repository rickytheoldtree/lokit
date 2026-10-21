using Lokit.Utils;
using Velopack;

namespace Lokit
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            //Velopack 的安装、更新、卸载钩子会以特殊参数启动程序，必须最先处理，处理完会直接退出
            VelopackApp.Build().SetArgs(args).Run();

            if (UpdateUtils.GetInstalledDataPath() is { } dataPath)
                PathUtils.UseDataPath(dataPath, UpdateUtils.LegacyDataPath);

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
