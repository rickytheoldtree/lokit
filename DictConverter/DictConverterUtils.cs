using DictConverter;
using LocalizationTranslationWPF.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace LocalizationTranslationWPF.DictConverter
{
    public static class DictConverterUtils
    {
        private static string DllPath => PathUtils.DictConverterDllPath;

        public static List<DictConverterType> Converters => dictConverterTypes ??= Init();

        private static List<DictConverterType>? dictConverterTypes;

        private static List<DictConverterType> Init()
        {
            var types = new List<DictConverterType>();
            //加载当前程序集
            Collect(Assembly.GetExecutingAssembly(), types);

            //加载path下所有dll
            if (!Directory.Exists(DllPath))
            {
                return types;
            }
            foreach (var file in Directory.GetFiles(DllPath, "*.dll"))
            {
                try
                {
                    Collect(Assembly.LoadFile(Path.GetFullPath(file)), types);
                }
                catch (Exception e)
                {
                    LogUtils.LogError($"加载{file}失败：{e.Message}");
                }
            }
            return types;
        }

        private static void Collect(Assembly assembly, List<DictConverterType> types)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!type.GetInterfaces().Contains(typeof(IDictConverter.IDictConverter))) continue;
                types.Add(new DictConverterType(type));
            }
        }

        public static Type GetConverterType(Type? type)
        {
            var t = Converters.FirstOrDefault(c => c.type == type).type;
            return t ?? typeof(DefaultConverter);
        }

        public static IDictConverter.IDictConverter? GetConverterInstance(Type? type)
        {
            if (type == null)
            {
                return null;
            }
            try
            {
                return Activator.CreateInstance(type) as IDictConverter.IDictConverter;
            }
            catch (Exception e)
            {
                LogUtils.LogError($"创建转换器{type.Name}失败：{e.Message}");
                return null;
            }
        }
    }
}
