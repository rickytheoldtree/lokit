using System.Diagnostics;
using System.IO;

namespace LocalizationTranslationWPF.Utils
{
    public static class PathUtils
    {
        public const string DataPath = "data";
        public const string ConfigPath = DataPath + "\\config.json";
        public const string IsoPath = DataPath + "\\iso";
        public const string DriverPath = DataPath + "\\web-drivers";
        public const string DictConverterDllPath = DataPath + "\\converters";

        public static void MakeSureFolderExist()
        {
            foreach (var path in new[] { IsoPath, DriverPath, DictConverterDllPath }) Directory.CreateDirectory(path);
        }
        internal static void OpenIsoFolder() => OpenFolder(IsoPath);
        internal static void OpenDictConverterFolder() => OpenFolder(DictConverterDllPath);
        public static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }
    }
}
