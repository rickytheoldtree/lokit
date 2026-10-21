namespace Lokit.DictConverter
{
#pragma warning disable CA1051 // The public field is part of the persisted converter descriptor.

    [Serializable]
    public struct DictConverterType
    {
        public string Name { get; }
        public Type type;

        public DictConverterType(Type type)
        {
            this.type = type;
            if (type == null)
            {
                Name = "未知";
                return;
            }

            try
            {
                Name = Activator.CreateInstance(type) is IDictConverter.IDictConverter converter
                    ? converter.NameForShow
                    : type.Name;
            }
            catch (Exception)
            {
                Name = type.Name;
            }
        }
    }

#pragma warning restore CA1051
}
