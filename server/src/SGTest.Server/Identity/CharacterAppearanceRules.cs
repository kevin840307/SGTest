namespace SGTest.Server.Identity;

/// <summary>
/// Trusted-server creation allowlist, never inferred from client asset counts.
/// Callers supply source-audited choices; this type does not certify source data.
/// </summary>
public sealed class CharacterAppearanceRules
{
    private readonly HashSet<CharacterAppearance> _allowed;
    public string SourceReference { get; }
    public IReadOnlyList<CharacterAppearance> Choices { get; }

    public CharacterAppearanceRules(string sourceReference, IEnumerable<CharacterAppearance> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (string.IsNullOrWhiteSpace(sourceReference) || sourceReference.Length > 384 ||
            sourceReference.Any(char.IsControl))
            throw new ArgumentException("Appearance source reference required", nameof(sourceReference));
        _allowed = new HashSet<CharacterAppearance>();
        foreach (var choice in choices)
        {
            // Storage bounds only. The exact allowed tuples are source-owned.
            if (choice.Race is < 0 or > 4 || choice.HairIndex is < 0 or > 65535 ||
                choice.HairColor is < 0 or > 7 || !_allowed.Add(choice) || _allowed.Count > 4096)
                throw new ArgumentException("Invalid, duplicate or oversized appearance choices", nameof(choices));
        }
        if (_allowed.Count == 0)
            throw new ArgumentException("Appearance choices cannot be empty", nameof(choices));
        SourceReference = sourceReference;
        Choices = Array.AsReadOnly(_allowed.OrderBy(c => c.Race).ThenBy(c => c.HairIndex)
            .ThenBy(c => c.HairColor).ToArray());
    }

    internal CharacterAppearanceState Resolve(CharacterAppearance requested)
    {
        if (!_allowed.Contains(requested))
            throw new ArgumentException("Appearance is not permitted by creation rules", nameof(requested));
        return new(requested, SourceReference);
    }
}
