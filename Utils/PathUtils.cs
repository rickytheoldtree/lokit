using System.Diagnostics;
using System.IO;

namespace LocalizationTranslationWPF.Utils
{
    public static class PathUtils
    {
        public const string DataPath = "data";
        public const string ConfigPath = DataPath + "\\config.json";
        public const string InputPath = DataPath + "\\input";
        public const string OutputPath = DataPath + "\\output";
        public const string IsoPath = DataPath + "\\iso";
        public const string EditPath = DataPath + "\\edit";
        public const string NewPath = DataPath + "\\new";
        public const string DriverPath = DataPath + "\\web-drivers";
        public const string DictConverterDllPath = DataPath + "\\converters";
        public const string CombineSourcePath = DataPath + "\\combine_source";
        public const string CombineOutputPath = DataPath + "\\combine_output";

        /// <summary>
        /// 程序运行所需的所有文件夹
        /// </summary>
        private static readonly string[] RequiredFolders =
        {
            InputPath,
            OutputPath,
            IsoPath,
            DictConverterDllPath,
            CombineSourcePath,
            CombineOutputPath,
            EditPath,
            NewPath,
            DriverPath,
        };

        public static void MakeSureFolderExist()
        {
            foreach (var folder in RequiredFolders)
            {
                Directory.CreateDirectory(folder);
            }
        }

        public static string InputFile(string name) => $"{InputPath}\\{name}.json";
        public static string OutputFile(string name) => $"{OutputPath}\\{name}.json";
        public static string EditFile(string name) => $"{EditPath}\\{name}.json";
        public static string NewFile(string name) => $"{NewPath}\\{name}.json";
        public static string CombineSourceFile(string name) => $"{CombineSourcePath}\\{name}.json";
        public static string CombineOutputFile(string name) => $"{CombineOutputPath}\\{name}.json";

        public static void OpenInputFolder() => OpenFolder(InputPath);
        public static void OpenOutputFolder() => OpenFolder(OutputPath);
        internal static void OpenIsoFolder() => OpenFolder(IsoPath);
        internal static void OpenDictConverterFolder() => OpenFolder(DictConverterDllPath);
        internal static void OpenCombineSourceFolder() => OpenFolder(CombineSourcePath);
        internal static void OpenCombineOutputFolder() => OpenFolder(CombineOutputPath);
        internal static void OpenEditorFolder() => OpenFolder(EditPath);
        internal static void OpenNewFolder() => OpenFolder(NewPath);

        private static void OpenFolder(string path)
        {
            //文件夹不存在时 explorer 会打开“文档”，先补建一次
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }
    }
}
