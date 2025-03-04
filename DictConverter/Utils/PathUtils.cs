using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LocalizationTranslationWPF.Utils
{
    public static class PathUtils
    {
        public const string ConfigPath = "config.json";
        public const string InputPath = "data\\input";
        public const string OutputPath = "data\\output";
        public const string IsoPath = "data\\iso";
        public const string EditPath = "data\\edit";
        public const string DriverPath = "web-drivers";
        public const string DictConverterDllPath = "converters";
        public const string CombineSourcePath = "data\\combine_source";
        public const string CombineOutputPath = "data\\combine_output";


        public static void MakeSureFolderExist()
        {
            if (!Directory.Exists(InputPath))
            {
                Directory.CreateDirectory(InputPath);
            }
            if (!Directory.Exists(OutputPath))
            {
                Directory.CreateDirectory(OutputPath);
            }
            if (!Directory.Exists(IsoPath))
            {
                Directory.CreateDirectory(IsoPath);
            }
            if (!Directory.Exists(DictConverterDllPath))
            {
                Directory.CreateDirectory(DictConverterDllPath);
            }
            if (!Directory.Exists(CombineSourcePath))
            {
                Directory.CreateDirectory(CombineSourcePath);
            }
            if (!Directory.Exists(CombineOutputPath))
            {
                Directory.CreateDirectory(CombineOutputPath);
            }
            if (!Directory.Exists(EditPath))
            {
                Directory.CreateDirectory(EditPath);
            }
        }
        public static void OpenOutputFolder()
        {
            System.Diagnostics.Process.Start("explorer.exe", OutputPath);
        }
        public static void OpenInputFolder()
        {
            System.Diagnostics.Process.Start("explorer.exe", InputPath);
        }
        internal static void OpenIsoFolder()
        {
            System.Diagnostics.Process.Start("explorer.exe", IsoPath);
        }

        internal static void OpenDictConverterFolder()
        {
            System.Diagnostics.Process.Start("explorer.exe", DictConverterDllPath);
        }

        internal static void OpenDriversFolder()
        {
            System.Diagnostics.Process.Start("explorer.exe", DriverPath);
        }

        internal static void OpenCombineSourceFolder()
        {
            System.Diagnostics.Process.Start("explorer.exe", CombineSourcePath);
        }
        internal static void OpenCombineOutputFolder()
        {
            System.Diagnostics.Process.Start("explorer.exe", CombineOutputPath);
        }

        internal static void OpenEditorFolder()
        {
            System.Diagnostics.Process.Start("explorer.exe", EditPath);
        }
    }
}
