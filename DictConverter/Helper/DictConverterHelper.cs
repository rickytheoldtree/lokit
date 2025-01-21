using DictConverter;
using LocalizationTranslationWPF.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LocalizationTranslationWPF.DictConverter
{
    public static class DictConverterHelper
    {
        private static string DllPath => PathHelper.DictConverterDllPath;
        public static List<DictConverterType> Converters
        {
            get => dictConverterTypes ??= Init();
        }

        private static List<DictConverterType>? dictConverterTypes;
        private static List<DictConverterType> Init()
        {
            var types = new List<DictConverterType>();
            //加载当前程序集
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.GetInterfaces().Contains(typeof(IDictConverter.IDictConverter)))
                {
                    types.Add(new DictConverterType(type));
                }
            }
            var rootPath = System.IO.Directory.GetCurrentDirectory();
            //加载path下所有dll
            foreach (var file in System.IO.Directory.GetFiles(DllPath, "*.dll"))
            {
                var dllPath = System.IO.Path.Combine(rootPath, file);
                var assembly = Assembly.LoadFile(dllPath);
                foreach (var type in assembly.GetTypes())
                {
                    if (type.GetInterfaces().Contains(typeof(IDictConverter.IDictConverter)))
                    {
                        types.Add(new DictConverterType(type));
                    }
                }
            }
            return types;
        }
        public static Type GetConverterType(Type type)
        {
            var t = Converters.FirstOrDefault(c => c.type == type).type;
            return t ?? typeof(DefaultConverter);
        }
        public static IDictConverter.IDictConverter? GetConverterInstance(Type type)
        {
            return Activator.CreateInstance(type) as IDictConverter.IDictConverter;
        }
    }
}
