using System.Text;

namespace SGTest.Server.Identity;

public readonly record struct CharacterCreationAttributes(
    int Strength, int Dexterity, int Constitution, int Wisdom);

// Public metadata only. Password material never appears in character lists/snapshots.
public sealed record CharacterCreationState(CharacterCreationAttributes Attributes, string SourceReference);

/// <summary>Trusted creation input. The secondary secret is deliberately redacted.</summary>
public sealed class CharacterCreationSettings
{
    public CharacterCreationAttributes Attributes { get; }
    public string SourceReference { get; }
    private readonly string _password;
    internal string PasswordForHashing => CharacterCreationRules.PasswordPurpose + _password;

    public CharacterCreationSettings(CharacterCreationAttributes attributes, string characterPassword,
        string sourceReference)
    {
        CharacterCreationRules.Validate(attributes);
        CharacterCreationRules.ValidatePassword(characterPassword);
        if (string.IsNullOrWhiteSpace(sourceReference) || sourceReference.Length > 384 ||
            sourceReference.Any(char.IsControl))
            throw new ArgumentException("Creation rules need a bounded source reference", nameof(sourceReference));
        Attributes = attributes;
        SourceReference = sourceReference;
        _password = characterPassword;
    }

    public override string ToString() => $"CharacterCreationSettings {{ Attributes = {Attributes}, Password = [REDACTED] }}";
}

/// <summary>Recovered2 creation choices, corroborated by Old's total-eight checks.</summary>
public static class CharacterCreationRules
{
    public const int MinimumAttribute = 1;
    public const int TotalAttributes = 8;
    public const int MaximumAttribute = TotalAttributes - 3 * MinimumAttribute;
    public const int MaximumNameWidth = 12;
    public const int PasswordCharacters = 4;
    public const int PasswordFailureLimit = 5;
    public const int PasswordLockoutSeconds = 60;
    internal const string PasswordPurpose = "SGTest/CharacterPassword/v1:";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void Validate(CharacterCreationAttributes value)
    {
        if (value.Strength is < MinimumAttribute or > MaximumAttribute ||
            value.Dexterity is < MinimumAttribute or > MaximumAttribute ||
            value.Constitution is < MinimumAttribute or > MaximumAttribute ||
            value.Wisdom is < MinimumAttribute or > MaximumAttribute ||
            (long)value.Strength + value.Dexterity + value.Constitution + value.Wisdom != TotalAttributes)
            throw new ArgumentException("Allocate all four points; each starting attribute is at least one", nameof(value));
    }

    public static void ValidateName(string normalizedName)
    {
        ArgumentNullException.ThrowIfNull(normalizedName);
        int width = 0;
        foreach (Rune rune in normalizedName.EnumerateRunes())
            width += rune.Value > 255 ? 2 : 1;
        if (width is < 1 or > MaximumNameWidth)
            throw new ArgumentException("Character name exceeds creation display width", nameof(normalizedName));
    }

    public static void ValidatePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        try { _ = StrictUtf8.GetByteCount(password); }
        catch (EncoderFallbackException) { throw new ArgumentException("Malformed character password", nameof(password)); }
        int length = 0;
        foreach (Rune rune in password.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
                throw new ArgumentException("Character password cannot contain controls", nameof(password));
            length++;
        }
        if (length != PasswordCharacters || password != password.Trim())
            throw new ArgumentException("Character password must contain four characters without edge whitespace", nameof(password));
    }
}

public enum CharacterPasswordStatus { Success, NotOwned, InvalidPassword, Locked }
