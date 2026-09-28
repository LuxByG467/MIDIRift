namespace MIDIRift.Modules.Themes;

public sealed class ThemeDefinition
{
    private readonly IReadOnlyDictionary<string, string> _tokens;

    public ThemeDefinition(string id, string name, IReadOnlyDictionary<string, string> tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tokens);
        Id = id;
        Name = name;
        _tokens = new Dictionary<string, string>(tokens, StringComparer.Ordinal);
    }

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyDictionary<string, string> Tokens => _tokens;
}
