using System.Diagnostics;
using System.IO;

namespace Lokit.Utils
{
    public static class PathUtils
    {
        /// <summary>
        /// 开发运行和回归测试使用工作目录下的 data；安装版启动时由 <see cref="UseDataPath"/> 换到程序目录之外。
        /// </summary>
        public static string DataPath { get; private set; } = "data";
        public static string ConfigPath => Path.Combine(DataPath, "config.json");
        public static string IsoPath => Path.Combine(DataPath, "iso");
        public static string DriverPath => Path.Combine(DataPath, "web-drivers");
        public static string DictConverterDllPath => Path.Combine(DataPath, "converters");
        private static string PendingImportPath => Path.Combine(DataPath, "pending-import");

        //只有这些是用户数据，浏览器驱动会按需重新下载
        private static readonly string[] ImportedFiles = { "config.json" };
        private static readonly string[] ImportedFolders = { "iso", "converters" };

        public static void MakeSureFolderExist()
        {
            foreach (var path in new[] { IsoPath, DriverPath, DictConverterDllPath }) Directory.CreateDirectory(path);
        }
        internal static void OpenIsoFolder() => OpenFolder(IsoPath);
        internal static void OpenDictConverterFolder() => OpenFolder(DictConverterDllPath);
        internal static void OpenDataFolder() => OpenFolder(DataPath);
        public static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }

        /// <summary>
        /// 安装版每次更新都会整体替换程序目录，所以数据放在别处。
        /// 必须在读取配置之前调用。
        /// </summary>
        /// <param name="legacyDataPath">改名前的数据目录，新目录第一次创建时自动导入</param>
        public static void UseDataPath(string dataPath, string? legacyDataPath = null)
        {
            var isFirstRun = !Directory.Exists(dataPath);
            DataPath = dataPath;
            Directory.CreateDirectory(DataPath);
            if (isFirstRun && legacyDataPath != null && LooksLikeDataFolder(legacyDataPath))
            {
                try
                {
                    StageImport(legacyDataPath);
                }
                catch (Exception e)
                {
                    LogUtils.LogError($"导入旧版本数据失败：{e.Message}");
                }
            }
            ApplyPendingImport();
            //程序自带的示例文件只补缺，不覆盖用户改过的同名文件
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "data"), DataPath, false);
        }

        /// <summary>
        /// 把旧版本 data 目录里的配置、ISO 和格式转换器暂存起来，下次启动读取配置前再覆盖进来，
        /// 避免当前进程退出时把内存里的配置写回去盖掉导入结果。
        /// </summary>
        /// <param name="source">旧版本的 data 目录，或包含 data 目录的程序目录</param>
        public static int StageImport(string source)
        {
            if (!LooksLikeDataFolder(source) && LooksLikeDataFolder(Path.Combine(source, "data")))
                source = Path.Combine(source, "data");
            if (!LooksLikeDataFolder(source))
                throw new InvalidOperationException("所选文件夹里没有 config.json 或 iso 文件夹，请选择旧版本程序旁边的 data 文件夹。");
            if (string.Equals(Path.GetFullPath(source).TrimEnd('\\'), Path.GetFullPath(DataPath).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("所选文件夹就是当前的数据目录。");

            if (Directory.Exists(PendingImportPath)) Directory.Delete(PendingImportPath, true);
            var count = 0;
            foreach (var file in ImportedFiles)
            {
                var from = Path.Combine(source, file);
                if (!File.Exists(from)) continue;
                Directory.CreateDirectory(PendingImportPath);
                File.Copy(from, Path.Combine(PendingImportPath, file));
                count++;
            }
            foreach (var folder in ImportedFolders)
                count += CopyDirectory(Path.Combine(source, folder), Path.Combine(PendingImportPath, folder), true);
            return count;
        }

        private static bool LooksLikeDataFolder(string path) =>
            File.Exists(Path.Combine(path, "config.json")) || Directory.Exists(Path.Combine(path, "iso"));

        private static void ApplyPendingImport()
        {
            if (!Directory.Exists(PendingImportPath)) return;
            try
            {
                var count = CopyDirectory(PendingImportPath, DataPath, true);
                Directory.Delete(PendingImportPath, true);
                LogUtils.Log($"已导入旧版本数据（{count} 个文件）");
            }
            catch (Exception e)
            {
                LogUtils.LogError($"导入旧版本数据失败：{e.Message}");
            }
        }

        private static int CopyDirectory(string source, string target, bool overwrite)
        {
            if (!Directory.Exists(source)) return 0;
            var count = 0;
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                if (!overwrite && File.Exists(destination)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite);
                count++;
            }
            return count;
        }
    }
}
