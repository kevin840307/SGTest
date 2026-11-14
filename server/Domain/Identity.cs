using System.Security.Cryptography;
using System.Text;

namespace SGTest.Server.Domain;

public sealed record AccountCredentials(long Id,string Username,byte[] Salt,byte[] PasswordHash);
public sealed record CharacterRecord(long Id,long AccountId,string Name,string NormalizedName);
public readonly record struct CharacterSummary(long Id,string Name);
public readonly record struct IdentityResult(bool Success,string Code,string Message,long AccountId=0,long CharacterId=0);

public interface IAccountStore
{
    AccountCredentials? FindCredentials(string normalizedUsername);
    bool TryCreateAccount(string username,string normalizedUsername,byte[] salt,byte[] passwordHash,out long accountId);
    CharacterSummary[] Characters(long accountId);
    CharacterRecord? Character(long accountId,long characterId);
    IdentityResult CreateCharacter(long accountId,string name,string normalizedName,int maximumCharacters);
}

public sealed class MemoryAccountStore : IAccountStore
{
    sealed class MemoryAccount(AccountCredentials credentials)
    {
        public AccountCredentials Credentials { get; }=credentials;
        public List<CharacterRecord> Characters { get; }=[];
    }

    readonly object gate=new();
    readonly Dictionary<string,MemoryAccount> byUsername=new(StringComparer.Ordinal);
    readonly Dictionary<long,MemoryAccount> byId=[];
    readonly HashSet<string> characterNames=new(StringComparer.Ordinal);
    long nextAccount,nextCharacter;

    public AccountCredentials? FindCredentials(string normalizedUsername)
    {
        lock(gate)
            return byUsername.TryGetValue(normalizedUsername,out var account) ? Clone(account.Credentials) : null;
    }

    public bool TryCreateAccount(string username,string normalizedUsername,byte[] salt,byte[] passwordHash,out long accountId)
    {
        lock(gate)
        {
            if(byUsername.ContainsKey(normalizedUsername))
            {
                accountId=0;
                return false;
            }
            accountId=++nextAccount;
            var credentials=new AccountCredentials(accountId,username,[..salt],[..passwordHash]);
            var account=new MemoryAccount(credentials);
            byUsername.Add(normalizedUsername,account);
            byId.Add(accountId,account);
            return true;
        }
    }

    public CharacterSummary[] Characters(long accountId)
    {
        lock(gate)
            return byId.TryGetValue(accountId,out var account)
                ? account.Characters.Select(c=>new CharacterSummary(c.Id,c.Name)).ToArray()
                : [];
    }

    public CharacterRecord? Character(long accountId,long characterId)
    {
        lock(gate)
        {
            if(!byId.TryGetValue(accountId,out var account)) return null;
            var character=account.Characters.FirstOrDefault(c=>c.Id==characterId);
            return character is null ? null : character with {};
        }
    }

    public IdentityResult CreateCharacter(long accountId,string name,string normalizedName,int maximumCharacters)
    {
        lock(gate)
        {
            if(!byId.TryGetValue(accountId,out var account))
                return new(false,"not_authenticated","Account is not authenticated.");
            if(account.Characters.Count>=maximumCharacters)
                return new(false,"character_limit","Character limit reached.",accountId);
            if(characterNames.Contains(normalizedName))
                return new(false,"character_name_exists","Character name already exists.",accountId);
            long id=++nextCharacter;
            var character=new CharacterRecord(id,accountId,name,normalizedName);
            account.Characters.Add(character);
            characterNames.Add(normalizedName);
            return new(true,"character_created","Character created.",accountId,id);
        }
    }

    static AccountCredentials Clone(AccountCredentials value) =>
        new(value.Id,value.Username,[..value.Salt],[..value.PasswordHash]);
}

public sealed class AccountService(IAccountStore store,int passwordIterations=60000)
{
    public const int MaximumCharacters=4;
    const int SaltBytes=16,HashBytes=32;
    readonly IAccountStore store=store;
    readonly int passwordIterations=passwordIterations>=1000
        ? passwordIterations
        : throw new ArgumentOutOfRangeException(nameof(passwordIterations));

    static string NormalizeUsername(string value) => value.Trim().ToUpperInvariant();
    static string NormalizeCharacterName(string value) => value.Trim().ToUpperInvariant();

    public static bool ValidUsername(string value)
    {
        value=value.Trim();
        if(value.Length is <3 or >24) return false;
        return value.All(c=>char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    public static bool ValidPassword(string value) =>
        value.Length is >=8 and <=72 && !value.Any(char.IsControl);

    public static bool ValidCharacterName(string value)
    {
        value=value.Trim();
        if(value.Length is <2 or >16) return false;
        return value.All(c=>char.IsLetterOrDigit(c) || c is '_' or '-');
    }

    byte[] Hash(string password,byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password),salt,passwordIterations,
            HashAlgorithmName.SHA256,HashBytes);

    public IdentityResult Register(string username,string password)
    {
        username=username.Trim();
        if(!ValidUsername(username))
            return new(false,"invalid_username","Username must be 3-24 ASCII letters, digits, _ or -.");
        if(!ValidPassword(password))
            return new(false,"invalid_password","Password must be 8-72 characters.");
        string normalized=NormalizeUsername(username);
        byte[] salt=RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash=Hash(password,salt);
        return store.TryCreateAccount(username,normalized,salt,hash,out long id)
            ? new(true,"registered","Account created.",id)
            : new(false,"account_exists","Account already exists.");
    }

    public IdentityResult Login(string username,string password)
    {
        if(!ValidUsername(username) || !ValidPassword(password))
            return new(false,"invalid_credentials","Invalid username or password.");
        var account=store.FindCredentials(NormalizeUsername(username));
        if(account is null) return new(false,"account_not_found","Account does not exist.");
        byte[] candidate=Hash(password,account.Salt);
        return CryptographicOperations.FixedTimeEquals(candidate,account.PasswordHash)
            ? new(true,"authenticated","Login successful.",account.Id)
            : new(false,"invalid_credentials","Invalid username or password.");
    }

    public CharacterSummary[] Characters(long accountId) => store.Characters(accountId);

    public IdentityResult CreateCharacter(long accountId,string name)
    {
        name=name.Trim();
        if(!ValidCharacterName(name))
            return new(false,"invalid_character_name","Character name must be 2-16 letters, digits, _ or -.");
        return store.CreateCharacter(accountId,name,NormalizeCharacterName(name),MaximumCharacters);
    }

    public CharacterRecord? Character(long accountId,long characterId) =>
        store.Character(accountId,characterId);
}
