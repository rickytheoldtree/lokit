using System.Diagnostics;
using System.IO;
using System.Windows;
using Lokit.Utils;
using Microsoft.Win32;
using Velopack;
using Wpf.Ui.Controls;

namespace Lokit
{
    public partial class MainWindow
    {
        private UpdateInfo? availableUpdate;
        private bool isUpdateBusy;

        private void InitUpdate()
        {
            DataPathText.Text = Path.GetFullPath(PathUtils.DataPath);
            DataPathText.ToolTip = DataPathText.Text;

            if (!UpdateUtils.IsInstalled)
            {
                UpdateStatusText.Text = $"当前版本 {UpdateUtils.CurrentVersion}。不是通过安装包运行，无法自动更新。";
                BtnUpdate.Content = "下载页面";
                return;
            }

            UpdateStatusText.Text = $"当前版本 {UpdateUtils.CurrentVersion}";
            Loaded += async (_, _) => await CheckForUpdateAsync(true);
        }

        private async void BtnUpdateClick(object sender, RoutedEventArgs e)
        {
            if (!UpdateUtils.IsInstalled)
            {
                Process.Start(new ProcessStartInfo(UpdateUtils.ReleasesUrl) { UseShellExecute = true });
                return;
            }

            if (availableUpdate is { } update) await InstallUpdateAsync(update);
            else await CheckForUpdateAsync(false);
        }

        /// <param name="silent">启动时的自动检查：没有新版本或联网失败都不打扰用户</param>
        private async Task CheckForUpdateAsync(bool silent)
        {
            if (isUpdateBusy) return;
            isUpdateBusy = true;
            BtnUpdate.IsEnabled = false;
            UpdateStatusText.Text = $"当前版本 {UpdateUtils.CurrentVersion}，正在检查更新…";
            try
            {
                availableUpdate = await UpdateUtils.CheckAsync();
            }
            catch (Exception error)
            {
                UpdateStatusText.Text = $"当前版本 {UpdateUtils.CurrentVersion}，检查更新失败";
                if (silent) LogUtils.LogError($"检查更新失败：{error.Message}");
                else ShowInfoBar("检查更新失败", error.Message, InfoBarSeverity.Warning);
                return;
            }
            finally
            {
                isUpdateBusy = false;
                BtnUpdate.IsEnabled = true;
            }

            if (availableUpdate == null)
            {
                UpdateStatusText.Text = $"当前版本 {UpdateUtils.CurrentVersion}，已是最新";
                if (!silent) ShowSnackbar("已是最新版本", $"当前版本 {UpdateUtils.CurrentVersion}。",
                    ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
                return;
            }

            var version = availableUpdate.TargetFullRelease.Version;
            UpdateStatusText.Text = $"当前版本 {UpdateUtils.CurrentVersion}，可更新到 {version}";
            BtnUpdate.Content = "立即更新";
            LogUtils.Log($"发现新版本 {version}");

            var dialog = new ContentDialog(RootContentDialogHost)
            {
                Title = $"发现新版本 {version}",
                Content = ReleaseNotesOf(availableUpdate),
                PrimaryButtonText = "立即更新",
                CloseButtonText = "稍后",
                PrimaryButtonAppearance = ControlAppearance.Primary,
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync(CancellationToken.None) == ContentDialogResult.Primary)
                await InstallUpdateAsync(availableUpdate);
        }

        private static string ReleaseNotesOf(UpdateInfo update)
        {
            const int maxLength = 600;
            var notes = update.TargetFullRelease.NotesMarkdown?.Trim();
            if (string.IsNullOrEmpty(notes)) return "更新会下载新版本并重启程序。";
            return notes.Length > maxLength ? notes[..maxLength] + "…" : notes;
        }

        private async Task InstallUpdateAsync(UpdateInfo update)
        {
            if (isUpdateBusy || cts != null) return;
            //安装会直接结束进程，先像关闭窗口一样处理未保存的更改
            if (!await ResolveUnsavedChangesAsync(1, "更新程序", true) ||
                !await ResolveUnsavedChangesAsync(2, "更新程序", true))
            {
                return;
            }

            isUpdateBusy = true;
            using var tokenSource = new CancellationTokenSource();
            var popWindow = new PopWindow("正在下载更新，请稍候...", "取消更新", _ => tokenSource.Cancel()) { Owner = this };
            var lastReported = -1;
            try
            {
                SetUILock(true);
                popWindow.Show();
                LogUtils.Log($"开始下载 {update.TargetFullRelease.Version}");
                await UpdateUtils.DownloadAsync(update, percent =>
                {
                    //进度回调非常频繁，每 10% 记一条
                    if (percent / 10 == Interlocked.Exchange(ref lastReported, percent / 10)) return;
                    LogUtils.Log($"下载更新 {percent}%");
                }, tokenSource.Token);

                LogUtils.Log("下载完成，正在重启并安装更新");
                ConfigUtils.FlushPendingSave();
                UpdateUtils.ApplyAndRestart(update);
            }
            catch (OperationCanceledException)
            {
                LogUtils.Log("已取消更新");
            }
            catch (Exception error)
            {
                LogUtils.LogError($"更新失败：{error.Message}");
                ShowInfoBar("更新失败", error.Message, InfoBarSeverity.Error);
            }
            finally
            {
                popWindow.CloseFromOwner();
                SetUILock(false);
                isUpdateBusy = false;
            }
        }

        private void BtnOpenDataClick(object sender, RoutedEventArgs e) => PathUtils.OpenDataFolder();

        private async void BtnImportDataClick(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFolderDialog { Title = "选择旧版本程序旁的 data 文件夹" };
            if (picker.ShowDialog(this) != true) return;

            int count;
            try
            {
                count = PathUtils.StageImport(picker.FolderName);
            }
            catch (Exception error)
            {
                ShowInfoBar("无法导入", error.Message, InfoBarSeverity.Warning);
                return;
            }

            var dialog = new ContentDialog(RootContentDialogHost)
            {
                Title = "导入将在重启后生效",
                Content = $"已准备导入 {count} 个文件，同名的配置和 ISO 文件会被覆盖。现在重启程序吗？",
                PrimaryButtonText = "立即重启",
                CloseButtonText = "稍后",
                PrimaryButtonAppearance = ControlAppearance.Primary,
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync(CancellationToken.None) != ContentDialogResult.Primary ||
                !await ResolveUnsavedChangesAsync(1, "重启程序", true) ||
                !await ResolveUnsavedChangesAsync(2, "重启程序", true))
            {
                return;
            }

            //等窗口关闭、配置写完后再启动新进程，新进程启动时才应用导入
            Application.Current.Exit += (_, _) => Process.Start(Environment.ProcessPath!);
            Close();
        }
    }
}
