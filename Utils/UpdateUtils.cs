using System.IO;
using System.Reflection;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace LocalizationTranslationWPF.Utils
{
    /// <summary>
    /// 通过 Velopack 从 GitHub Releases 检查和安装更新。只有用安装包或便携包运行时才可用。
    /// </summary>
    public static class UpdateUtils
    {
        public const string RepositoryUrl = "https://github.com/rickytheoldtree/game-json-localization-wpf";
        public const string ReleasesUrl = RepositoryUrl + "/releases";
        private const string AppDataFolderName = "LocalizationTranslationWPF";

        private static UpdateManager Manager => manager ??= new UpdateManager(new GithubSource(RepositoryUrl, null, false));
        private static UpdateManager? manager;

        /// <summary>
        /// 没经过 <see cref="Program.Main"/>（例如界面回归测试直接创建窗口）时 Velopack 未初始化，按未安装处理。
        /// </summary>
        public static bool IsInstalled => VelopackLocator.IsCurrentSet && Manager.IsInstalled;

        public static string CurrentVersion => (IsInstalled ? Manager.CurrentVersion?.ToString() : null) ??
                                               Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "未知";

        /// <summary>
        /// 安装版的数据放在 %AppData%，卸载时不会被删掉；便携版放在解压目录里，跟着文件夹走。
        /// 未安装（开发运行）时返回 null，继续使用工作目录下的 data。
        /// </summary>
        public static string? GetInstalledDataPath()
        {
            if (!IsInstalled) return null;
            if (Manager.IsPortable && VelopackLocator.Current.RootAppDir is { } root) return Path.Combine(root, "data");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataFolderName);
        }

        public static Task<UpdateInfo?> CheckAsync() => Manager.CheckForUpdatesAsync();

        public static Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken token) =>
            Manager.DownloadUpdatesAsync(update, progress, token);

        /// <summary>
        /// 立即退出并安装，调用前要处理好未保存的更改和待写入的配置。
        /// </summary>
        public static void ApplyAndRestart(UpdateInfo update) => Manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
}
