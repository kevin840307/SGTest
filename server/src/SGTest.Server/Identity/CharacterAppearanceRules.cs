using System.Text.Json;

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
                choice.HairColor is < 0 or > 7 || choice.BodyIndex is < 0 or > 255 ||
                choice.LegIndex is < 0 or > 255 || choice.SleeveColor is < 0 or > 255 ||
                !_allowed.Add(choice) || _allowed.Count > 7000)
                throw new ArgumentException("Invalid, duplicate or oversized appearance choices", nameof(choices));
        }
        if (_allowed.Count == 0)
            throw new ArgumentException("Appearance choices cannot be empty", nameof(choices));
        SourceReference = sourceReference;
        Choices = Array.AsReadOnly(_allowed.OrderBy(c => c.Race).ThenBy(c => c.HairIndex)
            .ThenBy(c => c.HairColor).ThenBy(c => c.BodyIndex).ThenBy(c => c.LegIndex)
            .ThenBy(c => c.SleeveColor).ToArray());
    }

    internal CharacterAppearanceState Resolve(CharacterAppearance requested)
    {
        if (!Allows(requested))
            throw new ArgumentException("Appearance is not permitted by creation rules", nameof(requested));
        return new(requested, SourceReference);
    }

    public bool Allows(CharacterAppearance requested) => _allowed.Contains(requested);

    public static CharacterAppearanceRules LoadVerified(string path)
    {
        var bytes=File.ReadAllBytes(path);
        if(bytes.Length is <1 or >1024*1024)
            throw new InvalidDataException("Appearance rule file size out of range");
        try
        {
            using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions { MaxDepth=8 });
            var root=document.RootElement;
            UniqueFields(root);
            if(root.GetProperty("kind").GetString()!="verified_character_appearance_rules" ||
                root.GetProperty("version").GetInt32()!=1 ||
                root.GetProperty("rules_verified").ValueKind!=JsonValueKind.True)
                throw new InvalidDataException("Appearance rules require explicit verification");
            string source=root.GetProperty("source_reference").GetString() ?? string.Empty;
            var choices=root.GetProperty("choices");
            if(choices.ValueKind!=JsonValueKind.Array || choices.GetArrayLength() is <1 or >7000)
                throw new InvalidDataException("Appearance choice count out of range");
            var values=new List<CharacterAppearance>();
            foreach(var choice in choices.EnumerateArray())
            {
                UniqueFields(choice);
                values.Add(new(choice.GetProperty("race").GetInt32(),
                    choice.GetProperty("hair_index").GetInt32(),choice.GetProperty("hair_color").GetInt32(),
                    choice.TryGetProperty("body_index",out var body) ? body.GetInt32() : 0,
                    choice.TryGetProperty("leg_index",out var leg) ? leg.GetInt32() : 0,
                    choice.TryGetProperty("sleeve_color",out var sleeve) ? sleeve.GetInt32() : 0));
            }
            return new(source,values);
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or
            KeyNotFoundException or FormatException or ArgumentException)
        {
            throw new InvalidDataException("Invalid character appearance rule manifest",error);
        }
    }

    private static void UniqueFields(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Object)
            throw new InvalidDataException("Appearance rule must be an object");
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())
            if(!names.Add(property.Name)) throw new InvalidDataException("Duplicate appearance rule field");
    }
}
