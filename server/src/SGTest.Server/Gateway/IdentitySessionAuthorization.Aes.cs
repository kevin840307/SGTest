using SGTest.Server.Visibility;
using SGTest.Server.Trade;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.PlayerShop;
using SGTest.Server.Party;
using SGTest.Server.Npc;
using SGTest.Server.Combat;
using SGTest.Server.Chat;
using SGTest.Server.Hotkey;
using System.Buffers.Binary;
using System.Net;

using System.Net.Sockets;
using System.Security.Cryptography;

using System.Text;
using SGTest.Server.Identity;
using SGTest.Server.Inventory;
using SGTest.Server.Wire;
using SGTest.Server.World;

using SGTest.Server.Transport;

namespace SGTest.Server.Gateway;

public sealed partial class IdentitySessionAuthorization
{
    public async Task<AesWireConnectionResult> RunAuthenticatedAesSocketAsync(
        TcpClient client, AesCbcSettings settings, WorldResultRouter router,
        AesWireConnectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(router);
        var configuredOptions = options ?? new AesWireConnectionOptions();
        var bindingGate = new object();
        bool sessionFinished = false;
        GatewayConnection? boundConnection = null;
        using var gameplayGate = new SemaphoreSlim(1,1);
        using var tradeIngress = _trades is null ? null : new TradeCommandIngress(_trades, router);
        var playerShop = _playerShops is null ? null : new PlayerShopCommandHandler(_playerShops, router);

        IPAddress remoteAddress=((IPEndPoint?)client.Client.RemoteEndPoint)?.Address
            ??IPAddress.None;
        if(remoteAddress.IsIPv4MappedToIPv6)remoteAddress=remoteAddress.MapToIPv4();

        async ValueTask<GatewayConnection?> Authorize(Stream stream, CancellationToken token)
        {
            using var credentialCts =
                CancellationTokenSource.CreateLinkedTokenSource(token);
            credentialCts.CancelAfter(configuredOptions.AuthorizationTimeoutMilliseconds);
            var connection = await AuthorizeAndBindAsync(
                stream, credentialCts.Token, token, remoteAddress).ConfigureAwait(false);
            if (connection is null) return null;
            var binding = connection.Binding;
            bool attached;
            lock (bindingGate)
            {
                // Connection cancellation may win just before this callback returns.
                // Never attach after the socket runner's finally has already
                // released the session.
                attached = !sessionFinished && !token.IsCancellationRequested &&
                    router.TryAttach(connection);
                if (attached && _combat is not null &&
                    !_combat.ResumeIfAttached(
                        binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch))
                {
                    router.TryDetach(connection);
                    attached = false;
                }
                if (attached && _trades is not null &&
                    !_trades.TryRegisterGameplayGate(binding,gameplayGate))
                {
                    router.TryDetach(connection);
                    attached = false;
                }
                if (attached) boundConnection = connection;
            }
            if(attached&&_playerShops is not null)
            {
                try
                {
                    await _playerShops.AttachAsync(binding).ConfigureAwait(false);
                }
                catch
                {
                    lock(bindingGate)
                    {
                        if(ReferenceEquals(boundConnection,connection))
                            boundConnection=null;
                    }
                    router.TryDetach(connection);
                    _trades?.UnregisterGameplayGate(binding,gameplayGate);
                    attached=false;
                }
            }
            if(attached&&_clans is not null)
            {
                try
                {
                    await _clans.AttachAsync(binding,router).ConfigureAwait(false);
                }
                catch
                {
                    lock(bindingGate)
                    {
                        if(ReferenceEquals(boundConnection,connection))
                            boundConnection=null;
                    }
                    router.TryDetach(connection);
                    _trades?.UnregisterGameplayGate(binding,gameplayGate);
                    attached=false;
                }
            }
            if(attached&&_friends is not null)
            {
                try
                {
                    await _friends.AttachAsync(binding,router).ConfigureAwait(false);
                }
                catch
                {
                    lock(bindingGate)
                    {
                        if(ReferenceEquals(boundConnection,connection))
                            boundConnection=null;
                    }
                    router.TryDetach(connection);
                    _trades?.UnregisterGameplayGate(binding,gameplayGate);
                    attached=false;
                }
            }
            if(attached&&_pets is not null)
            {
                try
                {
                    await _pets.AttachAsync(binding,router).ConfigureAwait(false);
                }
                catch
                {
                    lock(bindingGate)
                    {
                        if(ReferenceEquals(boundConnection,connection))
                            boundConnection=null;
                    }
                    router.TryDetach(connection);
                    _trades?.UnregisterGameplayGate(binding,gameplayGate);
                    attached=false;
                }
            }
            if(attached&&_mechas is not null)
            {
                try
                {
                    await _mechas.AttachAsync(binding,router).ConfigureAwait(false);
                }
                catch
                {
                    lock(bindingGate)
                    {
                        if(ReferenceEquals(boundConnection,connection))
                            boundConnection=null;
                    }
                    router.TryDetach(connection);
                    _trades?.UnregisterGameplayGate(binding,gameplayGate);
                    attached=false;
                }
            }
            if(attached)
            {
                try
                {
                    var equipped=await _identity.ListEquippedItemsAsync(
                        binding.ActorId).ConfigureAwait(false);
                    if(!router.TryPostPlayerEquipmentWorldState(
                        PlayerEquipmentWorldStateUpdate.FromEquipped(
                            binding,0,equipped)))
                        throw new IOException("Player equipment AOI presentation queue is full");
                }
                catch
                {
                    lock(bindingGate)
                    {
                        if(ReferenceEquals(boundConnection,connection))
                            boundConnection=null;
                    }
                    router.TryDetach(connection);
                    _trades?.UnregisterGameplayGate(binding,gameplayGate);
                    attached=false;
                }
            }
            if (!attached)
            {
                if (_combat is not null)
                    await _combat.CloseAndDrainAsync(
                        binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
                connection.Close();
                await _world.TrustedDisconnectAsync(
                    binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                    .ConfigureAwait(false);
                return null;
            }
            return connection;
        }

        async Task Release(ConnectionBinding binding)
        {
            // No socket release may detach/advance a participant while its
            // confirmed SQLite trade is inside the two-party commit barrier.
            await gameplayGate.WaitAsync().ConfigureAwait(false);
            try
            {
            GatewayConnection? connection;
            lock (bindingGate)
            {
                connection = boundConnection;
                boundConnection = null;
            }
            // Drain committed combat BEFORE detaching the Gateway. The settlement
            // worker queues each reliable damage/death frame only after SQLite
            // commits, so reconnect cannot observe old HP and the live socket
            // cannot lose a committed result merely because release won a race.
            if (_combat is not null)
                await _combat.CloseAndDrainAsync(
                    binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
            // Online player shops cannot outlive the authenticated session.
            // The same gameplay gate excludes a concurrent buyer commit here.
            if(_playerShops is not null)
                await _playerShops.CloseForBindingAsync(binding,router)
                    .ConfigureAwait(false);
            // A pending trade is session-scoped. Disconnect before detaching the
            // route so the peer can receive one authoritative terminal event.
            _trades?.Disconnect(binding,router);
            _parties?.Disconnect(binding,router);
            if(_friends is not null)
                await _friends.DetachAsync(binding,router).ConfigureAwait(false);
            // Presentation cleanup is best-effort during shutdown/backpressure;
            // World Leave still hides attached Pet/Mecha state and a later attach heals durable state.
            _pets?.TryDetach(binding,router);
            _mechas?.TryDetach(binding,router);
            router.TryPostPlayerEquipmentWorldState(
                PlayerEquipmentWorldStateUpdate.Detached(binding));
            if (connection is not null) router.TryDetach(connection);

            // Keep the character registered while its final authoritative
            // coordinates are committed. This prevents a reconnect from reading
            // the old SQLite spawn in the small DB-write window.
            ActorSnapshot? final = await _world.TrustedPrepareDisconnectAsync(
                binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                .ConfigureAwait(false);
            try
            {
                if (final is { } snapshot)
                {
                    if (_positions is not null)
                        await _positions.FinalizeAsync(snapshot).ConfigureAwait(false);
                    else
                        await _identity.UpdateCharacterSpawnAsync(snapshot.ActorId,
                            new CharacterSpawn(snapshot.MapId, snapshot.X, snapshot.Y))
                            .ConfigureAwait(false);
                }
            }
            finally
            {
                var release = final ?? new ActorSnapshot(
                    binding.ActorId, 0, binding.ConnectionEpoch, binding.MapEpoch,
                    0, 0, 0, 0, 0, false);
                await _world.TrustedDisconnectAsync(
                    release.ActorId, release.ConnectionEpoch, release.MapEpoch)
                    .ConfigureAwait(false);
            }
            }
            finally
            {
                _trades?.UnregisterGameplayGate(binding,gameplayGate);
                gameplayGate.Release();
            }
        }

        try
        {
            var portal = _portals is null ? null : new NpcPortalHandler(
                _world, _portals, _combat, _playerShops, _trades, router, gameplayGate);
            // Repair and enhancement share this authenticated session's gameplay
            // gate with other commands; no new session or command queue is created.
            var npcItems = new NpcItemModificationHandler(
                _identity, _world, _combat, _pickupWorldGeneration,
                _repairs, _repairItemRules, _enhancers, _enhancementItemRules,
                router, gameplayGate);
            // Shop, storage and bank share the same authenticated gameplay
            // gate and result router as equipment and item modification.
            var npcEconomy = new NpcEconomyHandler(
                _identity, _world, _combat, _pickupWorldGeneration,
                _shops, _storages, _banks, router, gameplayGate);
            var npcQuests = new NpcQuestHandler(
                _identity, _world, _combat, _quests, _pickupWorldGeneration,
                router, gameplayGate);
            var pvpStates = new PvpStateHandler(
                _identity, _world, _combat, router, gameplayGate);
            var pvpCombat = new PvpCombatHandler(
                _identity, _world, _combat, _pickupWorldGeneration,
                router, gameplayGate);
            // Inventory acquisition, equipment transfer and discard share the
            // authenticated socket's existing gameplay lock and DB authority.
            var inventory = new InventoryCommandHandler(
                _identity, _world, _combat, _equipmentRules,
                _pickupWorldGeneration, router, gameplayGate,
                _equipmentCombatRules,_progressionRules);
            var recovery = new PlayerRecoveryHandler(
                _identity, _world, _combat, _recoveryRules,
                _pickupWorldGeneration, router, gameplayGate);
            var skills = new SkillCommandHandler(
                _identity, _world, _combat, _pickupWorldGeneration,
                router, gameplayGate);
            var hotkeys = new HotkeyCoordinator(_identity);
            var profileProgression=_progressionRules is null
                ?null:new CharacterProfileProgressionHandler(
                    _identity,_progressionRules,router);
            return await AesWireConnection.RunServerAsync(client, settings,
                Authorize, _world.TryPostMove,
                configuredOptions with { EnforceAuthorizationTimeout = false },
                cancellationToken, Release,
                portal is null ? null : portal.HandleAsync, router.TryRequestResync,
                router.TryRequestNormalAttack,router.TryRequestPetAttack,
                router.TryRequestPetSkill,recovery.HandleRespawnAsync,
                inventory.HandlePickupAsync, recovery.HandleRecoveryItemAsync,
                inventory.HandleDiscardAsync,
                handleSkill:skills.HandleSkillAsync,handleShop:npcEconomy.HandleShopAsync,
                handleStorage:npcEconomy.HandleStorageAsync,
                handleEquipment:inventory.HandleEquipmentAsync,
                handleBank:npcEconomy.HandleBankAsync,
                handleRepair:npcItems.HandleRepairAsync,
                handleEnhancement:npcItems.HandleEnhancementAsync,
                handleTrade:tradeIngress is null ? null : tradeIngress.HandleAsync,
                handleQuest:npcQuests.HandleQuestAsync,
                handlePvp:pvpStates.HandleModeAsync,
                handlePvpAttack:pvpCombat.HandleAttackAsync,
                handlePvpSkill:pvpCombat.HandleSkillAsync,
                handleGmPvpName:pvpStates.HandleGmNameAsync,
                handlePlayerShop: playerShop is null ? null : playerShop.HandleAsync,
                handleChat:request=>ChatRouting.RouteAsync(
                    request,router,_parties,
                    (owner,target)=>_identity.IsIgnoredAsync(owner,target),_clans,
                    (left,right)=>_identity.IsFriendAsync(left,right),
                    (sender,connection,sequence,target,message)=>
                        _identity.QueueFriendOfflineMessageAsync(
                            sender,connection,sequence,target,message)),
                handleParty: _parties is null ? null : request => _parties.HandleAsync(request, router),
                handleClan: _clans is null ? null : request => _clans.HandleAsync(request, router),
                handleFriend: _friends is null ? null : request => _friends.HandleAsync(request, router),
                handleHotkey: request => hotkeys.HandleAsync(request,router),
                handlePet: _pets is null ? null : request => _pets.HandleAsync(request, router),
                handleMecha: _mechas is null ? null : request => _mechas.HandleAsync(request, router),
                handleAttribute: profileProgression is null ? null : profileProgression.HandleAttributeAsync)
                .ConfigureAwait(false);
        }
        finally
        {
            // Also covers a rare outer WaitAsync timeout while the auth callback
            // has already attached a Gateway but has not yet returned it.
            GatewayConnection? abandoned;
            lock (bindingGate)
            {
                sessionFinished = true;
                abandoned = boundConnection;
                boundConnection = null;
            }
            if (abandoned is not null)
            {
                await gameplayGate.WaitAsync().ConfigureAwait(false);
                try
                {
                var binding = abandoned.Binding;
                if (_combat is not null)
                    await _combat.CloseAndDrainAsync(
                        binding.ActorId, binding.ConnectionEpoch).ConfigureAwait(false);
                if(_playerShops is not null)
                    await _playerShops.CloseForBindingAsync(binding,router)
                        .ConfigureAwait(false);
                _trades?.Disconnect(binding,router);
                _parties?.Disconnect(binding,router);
                if(_friends is not null)
                    await _friends.DetachAsync(binding,router).ConfigureAwait(false);
                router.TryDetach(abandoned);
                await _world.TrustedDisconnectAsync(
                    binding.ActorId, binding.ConnectionEpoch, binding.MapEpoch)
                    .ConfigureAwait(false);
                }
                finally
                {
                    _trades?.UnregisterGameplayGate(abandoned.Binding,gameplayGate);
                    gameplayGate.Release();
                }
            }
        }
    }

}
