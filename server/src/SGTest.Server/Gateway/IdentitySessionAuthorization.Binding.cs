using SGTest.Server.Visibility;
using SGTest.Server.Trade;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.PlayerShop;
using SGTest.Server.Party;
using SGTest.Server.Npc;
using SGTest.Server.Combat;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

/// <summary>Authenticated account/character binding to the trusted gateway and world lifecycle.</summary>
public sealed partial class IdentitySessionAuthorization
{
    public ValueTask<GatewayConnection?> AuthorizeAndBindAsync(
        Stream tls, CancellationToken cancellationToken,
        IPAddress? remoteAddress = null) =>
        AuthorizeAndBindAsync(
            tls, cancellationToken, cancellationToken, remoteAddress);

    private async ValueTask<GatewayConnection?> AuthorizeAndBindAsync(
        Stream tls, CancellationToken authenticationCancellationToken,
        CancellationToken postAuthenticationCancellationToken,
        IPAddress? remoteAddress)
    {
        ArgumentNullException.ThrowIfNull(tls);
        var cancellationToken = authenticationCancellationToken;
        IPAddress source=remoteAddress??IPAddress.None;
        if(source.IsIPv4MappedToIPv6)source=source.MapToIPv4();

        byte[]? authFrame=await ReadFrameAsync(
            tls,cancellationToken).ConfigureAwait(false);
        if(authFrame is null)return null;

        if(WireProtocol.TryDecodeAccountRegisterRequest(
               authFrame,out var registrationRequest))
        {
            AccountRegisterWireStatus registrationStatus;
            if(_registrationRateLimiter is null)
                registrationStatus=AccountRegisterWireStatus.Unavailable;
            else if(!_registrationRateLimiter.TryAcquire(source))
                registrationStatus=AccountRegisterWireStatus.RateLimited;
            else
            {
                try
                {
                    var created=await _identity.CreateAccountAsync(
                        registrationRequest.Username,registrationRequest.Password,
                        cancellationToken).ConfigureAwait(false);
                    registrationStatus=created.Status switch
                    {
                        AccountCreateStatus.Created =>
                            AccountRegisterWireStatus.Created,
                        AccountCreateStatus.DuplicateUsername =>
                            AccountRegisterWireStatus.UsernameTaken,
                        _ => throw new InvalidOperationException(
                            "Unsupported account-create status")
                    };
                }
                catch(ArgumentException)
                {
                    registrationStatus=AccountRegisterWireStatus.InvalidInput;
                }
            }
            await WriteFrameAsync(tls,
                WireProtocol.EncodeAccountRegisterResult(
                    new(registrationStatus)),cancellationToken)
                .ConfigureAwait(false);
            if(registrationStatus!=AccountRegisterWireStatus.Created)
                return null;

            // Registration only creates durable credentials. Authentication is
            // deliberately a second explicit step on the SAME mandatory authenticated transport
            // connection so account creation never grants gameplay authority.
            authFrame=await ReadFrameAsync(
                tls,cancellationToken).ConfigureAwait(false);
            if(authFrame is null)return null;
        }

        if(!WireProtocol.TryDecodeAuthRequest(authFrame,out var request))
            return null;

        if(_rateLimiter is not null&&
           !_rateLimiter.IsAllowed(source,request.Username))
        {
            await WriteFrameAsync(tls,
                WireProtocol.EncodeAuthResult(new(AuthWireStatus.InvalidCredentials)),
                cancellationToken).ConfigureAwait(false);
            return null;
        }

        var authentication = await _identity.AuthenticateAsync(
            request.Username, request.Password, cancellationToken).ConfigureAwait(false);
        if(_rateLimiter is not null)
        {
            if(authentication.Status==AuthenticationStatus.InvalidCredentials)
                _rateLimiter.RecordFailure(source,request.Username);
            else
                _rateLimiter.RecordCredentialSuccess(request.Username);
        }
        AuthWireStatus authStatus = authentication.Status switch
        {
            AuthenticationStatus.Success => AuthWireStatus.Success,
            AuthenticationStatus.Disabled => AuthWireStatus.Disabled,
            _ => AuthWireStatus.InvalidCredentials
        };
        await WriteFrameAsync(tls, WireProtocol.EncodeAuthResult(new(authStatus)), cancellationToken)
            .ConfigureAwait(false);
        if (authentication.Status != AuthenticationStatus.Success || authentication.Account is null)
            return null;

        // Credential authentication is complete. Character selection/creation is
        // an authenticated player interaction and must not inherit the short
        // pre-authentication timeout. From this point onward only the socket/
        // server lifetime cancellation token may end the selection phase.
        cancellationToken = postAuthenticationCancellationToken;

        var account = authentication.Account;
        string? gmOperatorId=_gmAccounts.Contains(account.Username)
            ? "account:"+account.AccountId.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            : null;
        var characters = account.Characters.ToList();

        async Task SendCharacterListAsync()
        {
            var summaries = characters
                .Select(c => new CharacterSummaryWire(
                    c.CharacterId, checked((byte)c.Slot), c.Name,c.Appearance?.Value))
                .ToArray();
            await WriteFrameAsync(tls,
                WireProtocol.EncodeCharacterList(new(summaries,_characterCreation?.AppearanceRules?.Choices)),
                cancellationToken).ConfigureAwait(false);
        }

        async Task HandleCharacterManageAsync(CharacterManageWire manage,long accountId)
        {
            var status=manage.Action==CharacterManageAction.ChangePassword
                ? await _identity.ChangeCharacterPasswordAsync(
                    accountId,manage.CharacterId,manage.OldPassword,
                    manage.NewPassword!,cancellationToken).ConfigureAwait(false)
                : await _identity.DeleteCharacterWithPasswordAsync(
                    accountId,manage.CharacterId,manage.OldPassword!,
                    cancellationToken).ConfigureAwait(false);
            await WriteFrameAsync(tls,WireProtocol.EncodeCharacterManageResult(
                new(manage.Action,(CharacterManageStatus)status,manage.CharacterId)),
                cancellationToken).ConfigureAwait(false);
            if(status==CharacterManagementStatus.Success)
            {
                characters=(await _identity.ListCharactersAsync(
                    accountId,cancellationToken).ConfigureAwait(false)).ToList();
                await SendCharacterListAsync().ConfigureAwait(false);
            }
        }

        await SendCharacterListAsync().ConfigureAwait(false);

        CharacterRecord? character = null;
        while (character is null)
        {
            byte[]? selectionFrame = await ReadFrameAsync(
                tls, cancellationToken).ConfigureAwait(false);
            if (selectionFrame is null) return null;

            if (WireProtocol.TryDecodeCharacterManage(selectionFrame,out var manage))
            {
                try { await HandleCharacterManageAsync(manage,account.AccountId); }
                finally { CryptographicOperations.ZeroMemory(selectionFrame); }
                continue;
            }

            if (WireProtocol.TryDecodeCharacterCreate(
                    selectionFrame, out var create))
            {
                CharacterCreateWireStatus wireStatus = CharacterCreateWireStatus.Unavailable;
                CharacterRecord? createdCharacter = null;
                if (_characterCreation is null)
                {
                    wireStatus = CharacterCreateWireStatus.Unavailable;
                }
                else if (_characterCreation.AppearanceRules is { } rules
                    ? create.Appearance is not { } look || !rules.Allows(look)
                    : create.Appearance is not null)
                {
                    wireStatus = CharacterCreateWireStatus.InvalidAppearance;
                }
                else
                {
                    CharacterCreationSettings? creationSettings = null;
                    if (create.Creation is { } creation)
                    {
                        try
                        {
                            creationSettings = new CharacterCreationSettings(
                                new(creation.Strength,creation.Dexterity,
                                    creation.Constitution,creation.Wisdom),
                                creation.CharacterPassword,
                                CharacterCreationRules.SourceReference);
                        }
                        catch (ArgumentException)
                        {
                            wireStatus = CharacterCreateWireStatus.InvalidCreationSettings;
                        }
                    }

                    if (create.Creation is null || creationSettings is not null)
                    {
                        try
                        {
                            CharacterCreateResult result;
                            if (creationSettings is not null)
                            {
                                result = await _identity.CreateCharacterWithSettingsAsync(
                                    account.AccountId,create.Slot,create.Name,
                                    _characterCreation.Spawn,creationSettings,
                                    create.Appearance,_characterCreation.AppearanceRules,
                                    cancellationToken,_characterCreation.InitialProfileRules)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                result = create.Appearance is { } appearance
                                    ? await _identity.CreateCharacterAsync(
                                        account.AccountId,create.Slot,create.Name,
                                        _characterCreation.Spawn,appearance,
                                        _characterCreation.AppearanceRules!,cancellationToken)
                                        .ConfigureAwait(false)
                                    : await _identity.CreateCharacterAsync(
                                        account.AccountId,create.Slot,create.Name,
                                        _characterCreation.Spawn,cancellationToken)
                                        .ConfigureAwait(false);
                            }
                            wireStatus = result.Status switch
                            {
                                CharacterCreateStatus.Created =>
                                    CharacterCreateWireStatus.Success,
                                CharacterCreateStatus.SlotOccupied =>
                                    CharacterCreateWireStatus.SlotOccupied,
                                CharacterCreateStatus.NameTaken =>
                                    CharacterCreateWireStatus.NameTaken,
                                CharacterCreateStatus.LimitReached =>
                                    CharacterCreateWireStatus.LimitReached,
                                _ => CharacterCreateWireStatus.Unavailable
                            };
                            createdCharacter = result.Character;
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            wireStatus = CharacterCreateWireStatus.InvalidSlot;
                        }
                        catch (ArgumentException)
                        {
                            wireStatus = CharacterCreateWireStatus.InvalidName;
                        }
                    }
                }

                if (wireStatus == CharacterCreateWireStatus.Success &&
                    createdCharacter is not null)
                {
                    characters = (await _identity.ListCharactersAsync(
                        account.AccountId, cancellationToken)
                        .ConfigureAwait(false)).ToList();
                    await WriteFrameAsync(tls,
                        WireProtocol.EncodeCharacterCreateResult(new(
                            wireStatus, createdCharacter.CharacterId,
                            checked((byte)createdCharacter.Slot),
                            createdCharacter.Name,createdCharacter.Appearance?.Value)),
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WriteFrameAsync(tls,
                        WireProtocol.EncodeCharacterCreateResult(new(
                            wireStatus, 0, create.Slot, string.Empty)),
                        cancellationToken).ConfigureAwait(false);
                }

                // Always refresh after a create attempt. This lets the Client
                // recover from a concurrent slot/name conflict without
                // reconnecting and keeps the authoritative list server-owned.
                await SendCharacterListAsync().ConfigureAwait(false);
                CryptographicOperations.ZeroMemory(selectionFrame);
                continue;
            }

            if (!WireProtocol.TryDecodeCharacterChoose(
                    selectionFrame, out var choose))
                return null;
            character = characters.FirstOrDefault(
                c => c.CharacterId == choose.CharacterId);
            if (character is null)
            {
                await WriteFrameAsync(tls,
                    WireProtocol.EncodeCharacterChooseResult(new(
                        CharacterChooseWireStatus.NotOwned,
                        0, 0, 0, 0, 0)), cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }
            CharacterChooseWireStatus passwordStatus;
            try
            {
                passwordStatus = (!character.RequiresCharacterPassword || choose.CharacterPassword is null)
                    ? CharacterChooseWireStatus.PasswordRequired
                    : await _identity.VerifyCharacterPasswordAsync(account.AccountId,
                        character.CharacterId,choose.CharacterPassword,cancellationToken).ConfigureAwait(false) switch
                    {
                        CharacterPasswordStatus.Success => CharacterChooseWireStatus.Success,
                        CharacterPasswordStatus.NotOwned => CharacterChooseWireStatus.NotOwned,
                        CharacterPasswordStatus.Locked => CharacterChooseWireStatus.PasswordLocked,
                        _ => CharacterChooseWireStatus.InvalidPassword
                    };
            }
            finally { CryptographicOperations.ZeroMemory(selectionFrame); }
            if (passwordStatus != CharacterChooseWireStatus.Success)
            {
                await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                    passwordStatus,0,0,0,0,0)),cancellationToken).ConfigureAwait(false);
                if (passwordStatus == CharacterChooseWireStatus.NotOwned) return null;
                character = null;
            }
        }

        CharacterCombatProfile? combat = await _identity.GetCharacterCombatProfileAsync(
            character.CharacterId, cancellationToken).ConfigureAwait(false);
        if (combat is null &&
            character.Creation is { } persistedCreation &&
            character.Appearance is { } persistedAppearance &&
            _characterCreation?.InitialProfileRules is { } initialRules &&
            initialRules.TryBuild(
                persistedAppearance.Value.Race, persistedCreation.Attributes, out var derived) &&
            derived is not null)
        {
            await _identity.SetCharacterCombatProfileAsync(
                character.CharacterId, derived, cancellationToken).ConfigureAwait(false);
            combat = await _identity.GetCharacterCombatProfileAsync(
                character.CharacterId, cancellationToken).ConfigureAwait(false);
        }
        CharacterExperienceState? experience = await _identity.GetCharacterExperienceAsync(
            character.CharacterId, cancellationToken).ConfigureAwait(false);
        bool combatEnabled = combat?.Data.Trust is
            CombatProfileTrust.Verified or CombatProfileTrust.SyntheticTest;
        TrustedCombatProfile? trustedCombat = !combatEnabled ? null : new(
            combat!.Data.Attack, combat.Data.Defense, combat.Data.Hit, combat.Data.Dodge,
            combat.Data.AttackRange, combat.Data.AttackCooldownTicks);
        CharacterPvpState pvpPresentation =
            await _identity.GetCharacterPvpStateAsync(
                character.CharacterId,cancellationToken).ConfigureAwait(false);

        // Do not cancel ONLY the waiter: the trusted World queue may finish
        // registration after the authenticated transport authorization timeout, leaking a character.
        ActorSnapshot? actor;
        try
        {
            actor = await _world.TrustedRegisterAsync(character.CharacterId,
                character.MapId, character.X, character.Y).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            actor = null;
        }
        if (actor is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                CharacterChooseWireStatus.WorldRejected, 0, 0, 0, 0, 0)), cancellationToken).ConfigureAwait(false);
            return null;
        }

        var snapshot = actor.Value;
        bool combatAttached = false;
        bool completed = false;
        try
        {
            if (combatEnabled && combat is not null && _combat is not null)
            {
                combatAttached = _combat.TryAttach(snapshot, combat.Data);
                if (!combatAttached)
                    throw new InvalidOperationException(
                        "Combat settlement rejected authenticated character generation");
                if (combat.Data.CurrentHp == 0)
                {
                    var dead = await _world.TrustedSetCombatDeadAsync(
                        snapshot.ActorId, snapshot.ConnectionEpoch, snapshot.MapEpoch, true)
                        .ConfigureAwait(false);
                    if (dead is null)
                        throw new InvalidOperationException(
                            "Stored dead combat state could not gate authoritative World actor");
                }
            }

            // Bounded durable inventory baseline is read on the authenticated transport async
            // authorization path, before the Gateway begins AOI egress.
            // Do not emit a partial/truncated snapshot. Item instances are
            // owned by the selected authenticated character only.
            var inventory = await _identity.ListInventoryItemsAsync(
                snapshot.ActorId,WireProtocol.InventorySnapshotMaxItems+1,
                cancellationToken).ConfigureAwait(false);
            if(inventory.Count>WireProtocol.InventorySnapshotMaxItems)
                throw new InvalidOperationException(
                    "Character inventory exceeds bounded V1 baseline capacity");

            var skills = await _identity.ListCharacterSkillsAsync(
                snapshot.ActorId,WireProtocol.SkillBookSnapshotMaxSkills+1,
                cancellationToken).ConfigureAwait(false);
            if(skills.Count>WireProtocol.SkillBookSnapshotMaxSkills)
                throw new InvalidOperationException(
                    "Character skill book exceeds bounded V1 baseline capacity");

            var equipment = await _identity.ListEquippedItemsAsync(
                snapshot.ActorId,cancellationToken).ConfigureAwait(false);
            if(equipment.Count>WireProtocol.EquipmentSnapshotMaxItems)
                throw new InvalidOperationException(
                    "Character equipment exceeds bounded V1 baseline capacity");

            cancellationToken.ThrowIfCancellationRequested();
            await WriteFrameAsync(tls, WireProtocol.EncodeCharacterChooseResult(new(
                CharacterChooseWireStatus.Success, snapshot.ActorId, snapshot.MapId,
                snapshot.X, snapshot.Y, snapshot.MapEpoch)), cancellationToken).ConfigureAwait(false);

            if(gmOperatorId is not null)
                await WriteFrameAsync(tls,
                    WireProtocol.EncodeGmCapability(new(true)),
                    cancellationToken).ConfigureAwait(false);

            if (combat is not null)
            {
                await WriteFrameAsync(tls, WireProtocol.EncodePlayerCombatState(new(
                    snapshot.MapEpoch,
                    combat.Data.CurrentHp, combat.Data.MaxHp,
                    combat.Data.CurrentMp, combat.Data.MaxMp,
                    combatEnabled)),
                    cancellationToken).ConfigureAwait(false);
            }

            // Write a final packet even for an empty inventory; client must
            // never confuse an empty but complete bag with incomplete data.
            const int chunkSize=WireProtocol.InventorySnapshotMaxItemsPerFrame;
            for(int start=0; start<inventory.Count; start+=chunkSize)
            {
                var group=inventory.Skip(start).Take(chunkSize)
                    .Select(item=>new InventoryItemWire(item.ItemUid,
                        item.ItemDefinitionId,item.Quantity,item.SourceDropId,
                        item.EnhancementLevel,item.DurabilityRaw??-1))
                    .ToArray();
                bool final=start+group.Length==inventory.Count;
                await WriteFrameAsync(tls,WireProtocol.EncodeInventorySnapshot(
                    new(snapshot.ActorId,start,final,group)),
                    cancellationToken).ConfigureAwait(false);
            }
            if(inventory.Count==0)
                await WriteFrameAsync(tls,WireProtocol.EncodeInventorySnapshot(
                    new(snapshot.ActorId,0,true,Array.Empty<InventoryItemWire>())),
                    cancellationToken).ConfigureAwait(false);

            // Learned skill state is Server-owned even when empty. Always send
            // a final baseline so the client can distinguish "no skills" from
            // an incomplete login.
            const int skillChunk=WireProtocol.SkillBookSnapshotMaxSkillsPerFrame;
            for(int start=0;start<skills.Count;start+=skillChunk)
            {
                var group=skills.Skip(start).Take(skillChunk)
                    .Select(skill=>new SkillBookEntryWire(skill.SkillId,skill.Level))
                    .ToArray();
                bool final=start+group.Length==skills.Count;
                await WriteFrameAsync(tls,WireProtocol.EncodeSkillBookSnapshot(
                    new(snapshot.ActorId,start,final,group)),
                    cancellationToken).ConfigureAwait(false);
            }
            // Zero learned skills deliberately emits no new V1 frame, preserving
            // existing login ordering. The client resolves an absent skill-book
            // baseline to authoritative empty when the following WorldReady arrives.
            // Preserve the legacy login frame sequence if equipment is entirely disabled.
            // Persisted equipped rows still force a baseline so a character can recover.
            if(_equipmentRules is not null || equipment.Count>0)
            {
                const int equipmentChunk=WireProtocol.EquipmentSnapshotMaxItemsPerFrame;
                for(int start=0;start<equipment.Count;start+=equipmentChunk)
                {
                    var group=equipment.Skip(start).Take(equipmentChunk)
                        .Select(item=>new EquipmentItemWire(
                            item.ItemUid,item.SlotId,item.ItemDefinitionId,item.SourceDropId,
                            item.EnhancementLevel,item.DurabilityRaw??-1))
                        .ToArray();
                    bool final=start+group.Length==equipment.Count;
                    await WriteFrameAsync(tls,WireProtocol.EncodeEquipmentSnapshot(
                        new(snapshot.ActorId,start,final,group)),
                        cancellationToken).ConfigureAwait(false);
                }
                if(equipment.Count==0)
                    await WriteFrameAsync(tls,WireProtocol.EncodeEquipmentSnapshot(
                        new(snapshot.ActorId,0,true,Array.Empty<EquipmentItemWire>())),
                        cancellationToken).ConfigureAwait(false);
            }

            // Creation attributes are independently authoritative and persisted
            // even before source-verified combat derivation exists. Emit them as
            // a narrow self-only baseline instead of fabricating HP/MP/combat data.
            if (character.Creation is not null)
            {
                var creationAttributes = character.Creation.Attributes;
                await WriteFrameAsync(tls, WireProtocol.EncodePlayerCreationProfileState(new(
                    snapshot.ActorId, creationAttributes.Strength, creationAttributes.Dexterity,
                    creationAttributes.Constitution, creationAttributes.Wisdom)),
                    cancellationToken).ConfigureAwait(false);
            }

            // Full profile baseline follows the creation baseline. ProfileOnlyVerified
            // is source-backed for identity/vitals but cannot admit attack range/cadence;
            // combat handler admission is intentionally decided separately above.
            if (combat is not null)
            {
                await WriteFrameAsync(tls, WireProtocol.EncodePlayerProfileState(
                    CharacterProfileProgressionHandler.BuildState(
                        combat.Data,snapshot.ActorId,experience?.Experience??0,
                        _progressionRules)),
                    cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var gateway = new GatewayConnection(
                snapshot,combatProfile:trustedCombat,
                displayName:character.Name,pvpState:pvpPresentation,
                gmOperatorId:gmOperatorId,
                appearance:character.Appearance?.Value);
            completed = true;
            return gateway;
        }
        finally
        {
            if (!completed)
            {
                if (combatAttached && _combat is not null)
                    await _combat.CloseAndDrainAsync(
                        snapshot.ActorId, snapshot.ConnectionEpoch).ConfigureAwait(false);
                await _world.TrustedDisconnectAsync(snapshot.ActorId,
                    snapshot.ConnectionEpoch, snapshot.MapEpoch).ConfigureAwait(false);
            }
        }
    }

}
