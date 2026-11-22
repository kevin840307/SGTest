using SGTest.Server.Visibility;
using SGTest.Server.Trade;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.PlayerShop;
using SGTest.Server.Party;
using SGTest.Server.Npc;
using SGTest.Server.Combat;
using SGTest.Server.Chat;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using SGTest.Server.Identity;
using SGTest.Server.Inventory;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public sealed record CharacterCreationPolicy(CharacterSpawn Spawn, CharacterAppearanceRules? AppearanceRules = null);

/// <summary>
/// authenticated transport-only registration/login/character-selection boundary. Public account
/// registration is opt-in and rate-limited; even a newly created account must
/// still pass the ordinary AuthRequest before receiving CharacterList. It never
/// trusts a client-supplied ActorId: CharacterChoose is matched against the
/// authenticated account, then persistent character_id enters trusted World lifecycle.
/// </summary>
public sealed partial class IdentitySessionAuthorization
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PortalCatalog? _portals;
    private readonly CharacterPositionCheckpoint? _positions;
    private readonly PlayerCombatSettlement? _combat;
    private readonly long _pickupWorldGeneration;
    private readonly IReadOnlyDictionary<int,RecoveryItemRule>? _recoveryRules;
    private readonly CharacterCreationPolicy? _characterCreation;
    private readonly NpcShopCatalog? _shops;
    private readonly NpcStorageCatalog? _storages;
    private readonly IReadOnlyDictionary<int,EquipmentRule>? _equipmentRules;
    private readonly NpcBankCatalog? _banks;
    private readonly NpcRepairCatalog? _repairs;
    private readonly IReadOnlyDictionary<int,RepairItemRule>? _repairItemRules;
    private readonly NpcEnhancementCatalog? _enhancers;
    private readonly IReadOnlyDictionary<int,EquipmentEnhancementItemRule>?
        _enhancementItemRules;
    private readonly NpcQuestCatalog? _quests;
    private readonly PlayerTradeCoordinator? _trades;
    private readonly PlayerShopCoordinator? _playerShops;
    private readonly PartyCoordinator? _parties;
    private readonly AuthenticationRateLimiter? _rateLimiter;
    private readonly AccountRegistrationRateLimiter? _registrationRateLimiter;
    private readonly HashSet<string> _gmAccounts;

    public IdentitySessionAuthorization(SqliteIdentityStore identity, ServerWorldHost world,
                                    PortalCatalog? portals = null,
                                    CharacterPositionCheckpoint? positions = null,
                                    PlayerCombatSettlement? combat = null,
                                     long pickupWorldGeneration = 0,
                                     IReadOnlyDictionary<int,RecoveryItemRule>? recoveryRules = null,
                                     CharacterCreationPolicy? characterCreation = null,
                                     NpcShopCatalog? shops = null,
                                     NpcStorageCatalog? storages = null,
                                     IReadOnlyDictionary<int,EquipmentRule>? equipmentRules = null,
                                     NpcBankCatalog? banks = null,
                                     NpcRepairCatalog? repairs = null,
                                     IReadOnlyDictionary<int,RepairItemRule>? repairItemRules = null,
                                     NpcEnhancementCatalog? enhancers = null,
                                     IReadOnlyDictionary<int,EquipmentEnhancementItemRule>?
                                         enhancementItemRules = null,
                                     NpcQuestCatalog? quests = null,
                                     PlayerTradeCoordinator? trades = null,
                                     PlayerShopCoordinator? playerShops = null,
                                     PartyCoordinator? parties = null,
                                     AuthenticationRateLimiter? rateLimiter = null,
                                     AccountRegistrationRateLimiter? registrationRateLimiter = null,
                                     IReadOnlySet<string>? gmAccounts = null)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _portals = portals;
        _positions = positions;
        _combat = combat;
        if (pickupWorldGeneration is < 0 or > 0x3fffffff)
            throw new ArgumentOutOfRangeException(nameof(pickupWorldGeneration));
        _pickupWorldGeneration = pickupWorldGeneration;
        _recoveryRules = recoveryRules;
        if (characterCreation is not null)
        {
            var spawn = characterCreation.Spawn;
            if (spawn.MapId <= 0 || !double.IsFinite(spawn.X) ||
                !double.IsFinite(spawn.Y))
                throw new ArgumentOutOfRangeException(nameof(characterCreation));
        }
        _characterCreation = characterCreation;
        _shops = shops;
        _storages = storages;
        _equipmentRules = equipmentRules;
        _banks = banks;
        _repairs = repairs;
        _repairItemRules = repairItemRules;
        _enhancers = enhancers;
        _enhancementItemRules = enhancementItemRules;
        _quests = quests;
        _trades = trades;
        _playerShops=playerShops;
        _parties=parties;
        _rateLimiter=rateLimiter;
        _registrationRateLimiter=registrationRateLimiter;
        if(_playerShops is not null&&_trades is null)
            throw new ArgumentException(
                "Player shop requires the authenticated gameplay-gate registry",
                nameof(playerShops));
        _gmAccounts=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(gmAccounts is not null)
            foreach(string account in gmAccounts)
            {
                if(!ValidGmAccountName(account))
                    throw new ArgumentException(
                        "GM account allowlist entries must be valid account usernames",
                        nameof(gmAccounts));
                _gmAccounts.Add(account);
            }
    }

    private static bool ValidGmAccountName(string value)
    {
        if(value.Length is <3 or >32)return false;
        foreach(char c in value)
            if(!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or
                >= '0' and <= '9' or '_'))
                return false;
        return true;
    }

    private static async Task<byte[]?> ReadFrameAsync(Stream tls, CancellationToken cancellationToken)
    {
        byte[] header = new byte[WireProtocol.HeaderBytes];
        try { await tls.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false); }
        catch (EndOfStreamException) { return null; }
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        if (length < WireProtocol.HeaderBytes || length > WireProtocol.MaximumFrameBytes) return null;
        byte[] frame = new byte[length];
        header.CopyTo(frame, 0);
        try
        {
            if (length > header.Length)
                await tls.ReadExactlyAsync(frame.AsMemory(header.Length), cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException) { return null; }
        return WireProtocol.TryReadHeader(frame, out _) ? frame : null;
    }

    private static async Task WriteFrameAsync(Stream tls, byte[] frame, CancellationToken cancellationToken)
    {
        await tls.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
