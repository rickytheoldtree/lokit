namespace IDictConverter
{
    public interface IDictConverter
    {
        string NameForShow { get; }

        string Serialize(Dictionary<string, string> dict);

        bool TryDeserialize(string text, out Dictionary<string, string>? dict);
    }
}
