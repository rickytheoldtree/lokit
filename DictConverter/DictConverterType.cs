using System;

namespace LocalizationTranslationWPF.DictConverter
{
    [Serializable]
    public struct DictConverterType
    {
        public string Name { get; }
        public Type type;
        public DictConverterType(Type type)
        {
            this.type = type;
            if(type == null)
            {
                Name = "未知";
                return;
            }
            var IDictConverter = Activator.CreateInstance(type) as IDictConverter.IDictConverter;
            if (IDictConverter == null)
            {
                Name = type.Name;
            }
            else
            {
                Name = IDictConverter.NameForShow;
            }
        }
    }
}
