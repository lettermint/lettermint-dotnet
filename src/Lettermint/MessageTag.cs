namespace Lettermint;

/// <summary>A reusable exact-match message tag.</summary>
public sealed record MessageTag
{
    private static readonly System.Text.RegularExpressions.Regex NamePattern = new("\\A[A-Za-z0-9_-]{1,32}\\z");
    private static readonly System.Text.RegularExpressions.Regex ValuePattern = new("\\A[A-Za-z0-9_-]{1,64}\\z");

    public string Name { get; }
    public string Value { get; }

    public MessageTag(string name, string value)
    {
        if (!NamePattern.IsMatch(name)) throw new ArgumentException("Message tag names must match ^[A-Za-z0-9_-]{1,32}$", nameof(name));
        if (name.StartsWith("__lettermint", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Message tag names must not start with __lettermint", nameof(name));
        if (!ValuePattern.IsMatch(value)) throw new ArgumentException("Message tag values must match ^[A-Za-z0-9_-]{1,64}$", nameof(value));
        Name = name;
        Value = value;
    }
}
