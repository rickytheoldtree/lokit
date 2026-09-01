namespace LocalizationTranslationWPF.Data
{
#pragma warning disable CA1051 // The public field is part of the persisted language mapping format.

    /// <summary>
    /// 一种语言的“文件名-ISO代码”对应关系
    /// </summary>
    public struct Pair
    {
        public Pair(string name, string isoCode)
        {
            Name = name;
            this.isoCode = isoCode;
        }

        public string Name { get; }
        public string isoCode;

        public override string ToString()
        {
            return Name;
        }
    }

#pragma warning restore CA1051
}
