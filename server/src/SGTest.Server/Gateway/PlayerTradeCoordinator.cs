using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

/// <summary>
/// Ephemeral two-player negotiation around a durable SQLite final commit.
/// Pending offers are intentionally not persisted; a disconnect/restart cancels
/// the negotiation. Only the final commit is durable/idempotent by TradeId.
/// </summary>
public sealed class PlayerTradeCoordinator
{
    private static readonly TimeSpan InviteLifetime=TimeSpan.FromSeconds(30);
    private readonly object _gate=new();
    private readonly SqliteIdentityStore _store;
    private readonly ServerWorldHost _world;
    private readonly IReadOnlyDictionary<int,TradeItemRule> _itemRules;
    private readonly PlayerCombatSettlement? _combat;
    // Bind each authenticated actor to the SAME gameplay semaphore used for
    // portal, item and socket release. A trade needs both actors' gates.
    private readonly Dictionary<long,(ConnectionBinding Binding,SemaphoreSlim Gate)> _sessionGates=new();
    private readonly Dictionary<long,Session> _sessions=new();
    private readonly Dictionary<long,long> _byActor=new();
    private long _nextTradeId=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()*1000;

    private sealed class Participant
    {
        public required ConnectionBinding Binding { get; init; }
        public uint LastSequence { get; set; }
        public long Money { get; set; }
        public bool Locked { get; set; }
        public bool Confirmed { get; set; }
        public Dictionary<long,TradeOfferItemWire> Items { get; }=new();
    }

    private sealed class Session
    {
        public required long TradeId { get; init; }
        public required Participant First { get; init; }
        public required Participant Second { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
        public uint Revision { get; set; }=1;
        public bool Accepted { get; set; }
        public bool CommitStarted { get; set; }

        public Participant? Find(long actorId)=>
            First.Binding.ActorId==actorId?First:
            Second.Binding.ActorId==actorId?Second:null;
        public Participant Other(long actorId)=>
            First.Binding.ActorId==actorId?Second:First;
    }

    public PlayerTradeCoordinator(
        SqliteIdentityStore store,ServerWorldHost world,
        IReadOnlyDictionary<int,TradeItemRule> itemRules,
        PlayerCombatSettlement? combat=null)
    {
        _store=store??throw new ArgumentNullException(nameof(store));
        _world=world??throw new ArgumentNullException(nameof(world));
        _itemRules=itemRules??throw new ArgumentNullException(nameof(itemRules));
        _combat=combat;
        if(itemRules.Count>100000)throw new ArgumentOutOfRangeException(nameof(itemRules));
    }

    /// <summary>Called only after an authenticated Gateway binding is attached.
    /// A stale TLS session must never replace a newer actor's semaphore.</summary>
    public bool TryRegisterGameplayGate(ConnectionBinding binding,SemaphoreSlim gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0)
            return false;
        lock(_gate)
        {
            if(_sessionGates.ContainsKey(binding.ActorId))return false;
            _sessionGates.Add(binding.ActorId,(binding,gate));
            return true;
        }
    }

    /// <summary>Move an authenticated session's registered gameplay gate to
    /// the new trusted map generation while the portal handler owns that gate.
    /// Never accept a stale or cross-connection rebinding.</summary>
    public bool TryAdvanceGameplayGate(
        ConnectionBinding before,ActorSnapshot after,SemaphoreSlim gate)
    {
        var next=ConnectionBinding.FromTrustedSnapshot(after);
        if(before.ActorId<=0||before.ConnectionEpoch<=0||
           before.ActorId!=next.ActorId||
           before.ConnectionEpoch!=next.ConnectionEpoch||
           next.MapEpoch<=before.MapEpoch)return false;
        lock(_gate)
        {
            if(!_sessionGates.TryGetValue(before.ActorId,out var old)||
               old.Binding!=before||!ReferenceEquals(old.Gate,gate))return false;
            _sessionGates[before.ActorId]=(next,gate);
            return true;
        }
    }

    public void UnregisterGameplayGate(ConnectionBinding binding,SemaphoreSlim gate)
    {
        lock(_gate)
        {
            if(_sessionGates.TryGetValue(binding.ActorId,out var prior)&&
               prior.Binding.ActorId==binding.ActorId&&
               prior.Binding.ConnectionEpoch==binding.ConnectionEpoch&&
               ReferenceEquals(prior.Gate,gate))
                _sessionGates.Remove(binding.ActorId);
        }
    }

    public bool TryResolveGameplayGate(
        ConnectionBinding binding,out SemaphoreSlim? gate)
    {
        lock(_gate)
        {
            if(_sessionGates.TryGetValue(binding.ActorId,out var current)&&
               current.Binding==binding)
            {
                gate=current.Gate;
                return true;
            }
            gate=null;
            return false;
        }
    }

    public bool TryResolveGameplayGates(
        ConnectionBinding first,ConnectionBinding second,
        out SemaphoreSlim? firstGate,out SemaphoreSlim? secondGate)
    {
        lock(_gate)
        {
            if(_sessionGates.TryGetValue(first.ActorId,out var a)&&
               _sessionGates.TryGetValue(second.ActorId,out var b)&&
               a.Binding==first&&b.Binding==second&&
               !ReferenceEquals(a.Gate,b.Gate))
            {
                firstGate=a.Gate;
                secondGate=b.Gate;
                return true;
            }
            firstGate=null;
            secondGate=null;
            return false;
        }
    }

    public bool IsTrading(long actorId)
    {
        lock(_gate)return _byActor.ContainsKey(actorId);
    }

    public async Task HandleAsync(
        TrustedTradeCommand command,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        var actor=await _world.TrustedGetActorAsync(command.ActorId).ConfigureAwait(false);
        if(actor is not { } current||
           current.ConnectionEpoch!=command.ConnectionEpoch||
           current.MapEpoch!=command.RequestMapEpoch)
        {
            CancelActor(command.ActorId,router,TradeTerminalStatus.StaleGeneration);
            return;
        }

        switch(command.Action)
        {
            case TradeCommandAction.Request:
                await RequestAsync(command,current,router).ConfigureAwait(false);
                return;
            case TradeCommandAction.Accept:
                await ReplyAsync(command,true,router).ConfigureAwait(false);
                return;
            case TradeCommandAction.Reject:
                await ReplyAsync(command,false,router).ConfigureAwait(false);
                return;
            case TradeCommandAction.AddItem:
                await AddItemAsync(command,router).ConfigureAwait(false);
                return;
            case TradeCommandAction.RemoveItem:
                RemoveItem(command,router);
                return;
            case TradeCommandAction.SetMoney:
                await SetMoneyAsync(command,router).ConfigureAwait(false);
                return;
            case TradeCommandAction.Lock:
                Lock(command,router);
                return;
            case TradeCommandAction.FinalConfirm:
                await ConfirmAsync(command,router).ConfigureAwait(false);
                return;
            case TradeCommandAction.Cancel:
                CancelCommand(command,router);
                return;
            default:
                return;
        }
    }

    public void Disconnect(ConnectionBinding binding,WorldResultRouter router)=>
        CancelActor(binding.ActorId,router,TradeTerminalStatus.PeerDisconnected,binding);

    private async Task RequestAsync(
        TrustedTradeCommand command,ActorSnapshot requester,WorldResultRouter router)
    {
        if(command.TargetActorId==command.ActorId)return;
        var target=await _world.TrustedGetActorAsync(command.TargetActorId).ConfigureAwait(false);
        // A dead character cannot initiate or receive a trade invitation.
        // This is the trusted World combat-death barrier, not a Client flag.
        if(requester.CombatDead||target is not { } peer||peer.CombatDead||
           peer.MapId!=requester.MapId)return;
        var firstBinding=ConnectionBinding.FromTrustedSnapshot(requester);
        var secondBinding=ConnectionBinding.FromTrustedSnapshot(peer);
        if(!router.IsCurrentBinding(firstBinding)||!router.IsCurrentBinding(secondBinding))return;

        Session? session=null;
        lock(_gate)
        {
            if(_byActor.ContainsKey(firstBinding.ActorId)||
               _byActor.ContainsKey(secondBinding.ActorId))return;
            long tradeId=Interlocked.Increment(ref _nextTradeId);
            session=new Session
            {
                TradeId=tradeId,
                First=new Participant{Binding=firstBinding,LastSequence=command.TradeSequence},
                Second=new Participant{Binding=secondBinding},
                ExpiresAt=DateTimeOffset.UtcNow+InviteLifetime
            };
            _sessions.Add(tradeId,session);
            _byActor.Add(firstBinding.ActorId,tradeId);
            _byActor.Add(secondBinding.ActorId,tradeId);
        }
        QueueBoth(session!,router);
        _=ExpireAsync(session!.TradeId,session.ExpiresAt,router);
    }

    private async Task ReplyAsync(
        TrustedTradeCommand command,bool accept,WorldResultRouter router)
    {
        Session? session;
        bool expired=false;
        lock(_gate)
        {
            if(!_sessions.TryGetValue(command.TradeId,out session))return;
            var self=session.Find(command.ActorId);
            if(self is null||self!=session.Second||session.Accepted||
               command.TradeSequence<=self.LastSequence)return;
            if(DateTimeOffset.UtcNow>=session.ExpiresAt)
            {
                session.Revision++;
                RemoveLocked(session);
                expired=true;
            }
            else if(!accept)
            {
                self.LastSequence=command.TradeSequence;
                session.Revision++;
                RemoveLocked(session);
            }
            else
            {
                self.LastSequence=command.TradeSequence;
                session.Accepted=true;
                session.Revision++;
            }
        }
        if(expired)
        {
            QueueTerminalBoth(session!,router,TradeTerminalStatus.Expired);
            return;
        }
        if(!accept)
        {
            QueueTerminalBoth(session!,router,TradeTerminalStatus.Rejected);
            return;
        }
        if(!await BindingsStillCurrentAsync(session!,router).ConfigureAwait(false))
        {
            CancelSession(session!,router,TradeTerminalStatus.PeerDisconnected);
            return;
        }
        QueueBoth(session!,router);
    }

    private async Task AddItemAsync(
        TrustedTradeCommand command,WorldResultRouter router)
    {
        Session? before;
        Participant? self;
        lock(_gate)
        {
            if(!TryMutable(command,out before,out self)||
               self!.Items.ContainsKey(command.ItemUid)||
               self.Items.Count>=WireProtocol.TradeMaxItemsPerSide)return;
        }
        var items=await _store.ListInventoryItemsAsync(command.ActorId,4096)
            .ConfigureAwait(false);
        var item=items.FirstOrDefault(x=>x.ItemUid==command.ItemUid);
        if(item is null||
           !_itemRules.TryGetValue(item.ItemDefinitionId,out var rule)||!rule.CanTrade)
            return;
        lock(_gate)
        {
            if(!_sessions.TryGetValue(command.TradeId,out var current)||
               !ReferenceEquals(current,before)||
               !TryMutable(command,out _,out self)||
               self!.Items.ContainsKey(command.ItemUid)||
               self.Items.Count>=WireProtocol.TradeMaxItemsPerSide)return;
            self.Items.Add(command.ItemUid,new(
                item.ItemUid,item.ItemDefinitionId,item.Quantity,item.SourceDropId,
                item.EnhancementLevel,item.DurabilityRaw??-1));
            self.LastSequence=command.TradeSequence;
            ResetLocks(current);
            current.Revision++;
        }
        QueueBoth(before!,router);
    }

    private void RemoveItem(TrustedTradeCommand command,WorldResultRouter router)
    {
        Session? session;
        lock(_gate)
        {
            if(!TryMutable(command,out session,out var self)||
               !self!.Items.Remove(command.ItemUid))return;
            self.LastSequence=command.TradeSequence;
            ResetLocks(session!);
            session!.Revision++;
        }
        QueueBoth(session!,router);
    }

    private async Task SetMoneyAsync(
        TrustedTradeCommand command,WorldResultRouter router)
    {
        Session? before;
        Participant? self;
        lock(_gate)
        {
            if(!TryMutable(command,out before,out self))return;
        }
        long wallet=await _store.GetCharacterMoneyAsync(command.ActorId).ConfigureAwait(false);
        if(command.Money>wallet)return;
        lock(_gate)
        {
            if(!_sessions.TryGetValue(command.TradeId,out var current)||
               !ReferenceEquals(current,before)||
               !TryMutable(command,out _,out self))return;
            self!.Money=command.Money;
            self.LastSequence=command.TradeSequence;
            ResetLocks(current);
            current.Revision++;
        }
        QueueBoth(before!,router);
    }

    private void Lock(TrustedTradeCommand command,WorldResultRouter router)
    {
        Session? session;
        lock(_gate)
        {
            if(!TryActive(command,out session,out var self)||self!.Locked)return;
            self.Locked=true;
            self.Confirmed=false;
            self.LastSequence=command.TradeSequence;
            session!.Revision++;
        }
        QueueBoth(session!,router);
    }

    private async Task ConfirmAsync(
        TrustedTradeCommand command,WorldResultRouter router)
    {
        Session? session;
        TradeCommitRequest? commit=null;
        lock(_gate)
        {
            if(!TryActive(command,out session,out var self)||
               !session!.First.Locked||!session.Second.Locked)return;
            self!.LastSequence=command.TradeSequence;
            self.Confirmed=true;
            session.Revision++;
            if(session.First.Confirmed&&session.Second.Confirmed&&!session.CommitStarted)
            {
                session.CommitStarted=true;
                commit=new TradeCommitRequest(
                    session.TradeId,session.First.Binding.ActorId,session.Second.Binding.ActorId,
                    session.First.Money,session.Second.Money,
                    session.First.Items.Keys.OrderBy(x=>x).ToArray(),
                    session.Second.Items.Keys.OrderBy(x=>x).ToArray());
            }
        }
        QueueBoth(session!,router);
        if(commit is null)return;

        // Serialize final settlement with BOTH authenticated socket gameplay
        // gates. FinalConfirm ingress must not hold its own gameplay gate, or
        // the two peers could deadlock while acquiring one another's gates.
        SemaphoreSlim? firstGate=null,secondGate=null;
        lock(_gate)
        {
            if(_sessionGates.TryGetValue(session!.First.Binding.ActorId,out var first)&&
               _sessionGates.TryGetValue(session.Second.Binding.ActorId,out var second)&&
               first.Binding==session.First.Binding&&
               second.Binding==session.Second.Binding)
            {
                firstGate=first.Gate;
                secondGate=second.Gate;
            }
        }
        if(firstGate is null||secondGate is null||ReferenceEquals(firstGate,secondGate))
        {
            AbortStaleCommit(session!,router);
            return;
        }

        // Stable actor-id order prevents opposite-direction dual-trade deadlock.
        if(session!.First.Binding.ActorId>session.Second.Binding.ActorId)
            (firstGate,secondGate)=(secondGate,firstGate);
        bool heldFirst=false,heldSecond=false;
        bool suspendedFirst=false,suspendedSecond=false;
        try
        {
            heldFirst=await firstGate.WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            if(!heldFirst)
            {
                AbortStaleCommit(session,router);
                return;
            }
            heldSecond=await secondGate.WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            if(!heldSecond)
            {
                AbortStaleCommit(session,router);
                return;
            }

            // The combat owner has a projected HP reservation timeline.
            // Drain every earlier hit and suspend NEW hits before authorizing
            // this SQLite commit, so lethal damage cannot overtake its guard.
            if(_combat is not null)
            {
                await _combat.SuspendAndDrainAsync(
                    session.First.Binding.ActorId,
                    session.First.Binding.ConnectionEpoch,
                    session.First.Binding.MapEpoch).ConfigureAwait(false);
                suspendedFirst=true;
                await _combat.SuspendAndDrainAsync(
                    session.Second.Binding.ActorId,
                    session.Second.Binding.ConnectionEpoch,
                    session.Second.Binding.MapEpoch).ConfigureAwait(false);
                suspendedSecond=true;
            }

            if(!await BindingsStillCurrentAsync(
                    session,router,requireSuspendedCombat:_combat is not null)
                .ConfigureAwait(false))
            {
                AbortStaleCommit(session,router);
                return;
            }

            // Final confirmation must settle exactly the item metadata both
            // peers last reviewed. Normal Clients already serialize enhancement
            // against an active trade, but the Server cannot trust that UI gate.
            // Both authenticated gameplay semaphores are held here, so no formal
            // item mutation can race this revalidation before the SQLite commit.
            if(!await OffersStillMatchInventoryAsync(session).ConfigureAwait(false))
            {
                lock(_gate)
                {
                    if(_sessions.TryGetValue(session.TradeId,out var active)&&
                       ReferenceEquals(active,session))RemoveLocked(session);
                }
                QueueTerminalBoth(session,router,TradeTerminalStatus.CommitFailed);
                return;
            }

            var result=await _store.CommitInventoryTradeAsync(commit,_itemRules)
                .ConfigureAwait(false);
            if(result.Status is TradeCommitStatus.Applied or TradeCommitStatus.AlreadyApplied)
            {
                lock(_gate)
                {
                    if(_sessions.TryGetValue(session.TradeId,out var active)&&
                       ReferenceEquals(active,session))RemoveLocked(session);
                }
                QueueTerminal(
                    session.First,session,router,TradeTerminalStatus.Success,
                    result.FirstWalletMoney);
                QueueTerminal(
                    session.Second,session,router,TradeTerminalStatus.Success,
                    result.SecondWalletMoney);
            }
            else
            {
                lock(_gate)
                {
                    if(_sessions.TryGetValue(session.TradeId,out var active)&&
                       ReferenceEquals(active,session))RemoveLocked(session);
                }
                QueueTerminalBoth(session,router,TradeTerminalStatus.CommitFailed);
            }
        }
        finally
        {
            // Resume while the trusted gameplay locks still exclude transfers.
            // Even a failed combat-generation resume must NEVER leak a semaphore
            // and strand both authenticated clients.
            bool resumed=true;
            try
            {
                if(_combat is not null)
                {
                    if(suspendedSecond)
                        resumed=_combat.TryResumeGeneration(
                            session.Second.Binding.ActorId,
                            session.Second.Binding.ConnectionEpoch,
                            session.Second.Binding.MapEpoch)&&resumed;
                    if(suspendedFirst)
                        resumed=_combat.TryResumeGeneration(
                            session.First.Binding.ActorId,
                            session.First.Binding.ConnectionEpoch,
                            session.First.Binding.MapEpoch)&&resumed;
                }
            }
            finally
            {
                if(heldSecond)secondGate.Release();
                if(heldFirst)firstGate.Release();
            }
            if(!resumed)
                throw new PlayerCombatSettlementException(
                    "Trade participant combat generation could not resume");
        }
    }

    private async Task<bool> OffersStillMatchInventoryAsync(Session session)
    {
        var first=await _store.ListInventoryItemsAsync(
            session.First.Binding.ActorId,4096).ConfigureAwait(false);
        var second=await _store.ListInventoryItemsAsync(
            session.Second.Binding.ActorId,4096).ConfigureAwait(false);
        return OffersMatch(session.First,first)&&OffersMatch(session.Second,second);
    }

    private static bool OffersMatch(
        Participant participant,IReadOnlyList<InventoryItem> inventory)
    {
        if(participant.Items.Count==0)return true;
        var byUid=inventory.ToDictionary(item=>item.ItemUid);
        foreach(var offer in participant.Items.Values)
        {
            if(!byUid.TryGetValue(offer.ItemUid,out var owned)||
               owned.ItemDefinitionId!=offer.ItemDefinitionId||
               owned.Quantity!=offer.Quantity||
               owned.SourceDropId!=offer.SourceDropId||
               owned.EnhancementLevel!=offer.EnhancementLevel||
               (owned.DurabilityRaw??-1)!=offer.DurabilityRaw)
                return false;
        }
        return true;
    }

    private void AbortStaleCommit(Session session,WorldResultRouter router)
    {
        lock(_gate)
        {
            if(!_sessions.TryGetValue(session.TradeId,out var active)||
               !ReferenceEquals(active,session))return;
            session.Revision++;
            RemoveLocked(session);
        }
        QueueTerminalBoth(session,router,TradeTerminalStatus.StaleGeneration);
    }

    private void CancelCommand(TrustedTradeCommand command,WorldResultRouter router)
    {
        Session? session;
        lock(_gate)
        {
            if(!_sessions.TryGetValue(command.TradeId,out session)||
               session.Find(command.ActorId) is not { } self||session.CommitStarted||
               command.TradeSequence<=self.LastSequence)return;
            self.LastSequence=command.TradeSequence;
            session.Revision++;
            RemoveLocked(session);
        }
        QueueTerminalBoth(session!,router,TradeTerminalStatus.Cancelled);
    }

    private bool TryMutable(
        TrustedTradeCommand command,out Session? session,out Participant? self)
    {
        if(!TryActive(command,out session,out self)||self!.Locked)return false;
        return true;
    }

    private bool TryActive(
        TrustedTradeCommand command,out Session? session,out Participant? self)
    {
        self=null;
        if(!_sessions.TryGetValue(command.TradeId,out session)||
           !session.Accepted||session.CommitStarted)return false;
        self=session.Find(command.ActorId);
        if(self is null||self.Binding.ConnectionEpoch!=command.ConnectionEpoch||
           self.Binding.MapEpoch!=command.RequestMapEpoch||
           command.TradeSequence<=self.LastSequence)return false;
        return true;
    }

    private static void ResetLocks(Session session)
    {
        session.First.Locked=false;
        session.First.Confirmed=false;
        session.Second.Locked=false;
        session.Second.Confirmed=false;
    }

    private async Task<bool> BindingsStillCurrentAsync(
        Session session,WorldResultRouter router,bool requireSuspendedCombat=false)
    {
        var first=await _world.TrustedGetActorAsync(session.First.Binding.ActorId)
            .ConfigureAwait(false);
        var second=await _world.TrustedGetActorAsync(session.Second.Binding.ActorId)
            .ConfigureAwait(false);
        return first is { } a&&second is { } b&&
            ConnectionBinding.FromTrustedSnapshot(a)==session.First.Binding&&
            ConnectionBinding.FromTrustedSnapshot(b)==session.Second.Binding&&
            a.MapId==b.MapId&&!a.CombatDead&&!b.CombatDead&&
            (!requireSuspendedCombat||(_combat is not null&&
                _combat.TryGetSuspendedCombatState(a,out int hpA,out _)&&hpA>0&&
                _combat.TryGetSuspendedCombatState(b,out int hpB,out _)&&hpB>0))&&
            router.IsCurrentBinding(session.First.Binding)&&
            router.IsCurrentBinding(session.Second.Binding);
    }

    private async Task ExpireAsync(
        long tradeId,DateTimeOffset expiresAt,WorldResultRouter router)
    {
        try
        {
            TimeSpan delay=expiresAt-DateTimeOffset.UtcNow;
            if(delay>TimeSpan.Zero)await Task.Delay(delay).ConfigureAwait(false);
            Session? session=null;
            lock(_gate)
            {
                if(_sessions.TryGetValue(tradeId,out var active)&&
                   !active.Accepted&&!active.CommitStarted&&
                   DateTimeOffset.UtcNow>=active.ExpiresAt)
                {
                    active.Revision++;
                    session=active;
                    RemoveLocked(active);
                }
            }
            if(session is not null)
                QueueTerminalBoth(session,router,TradeTerminalStatus.Expired);
        }
        catch{ }
    }

    private void CancelActor(
        long actorId,WorldResultRouter router,TradeTerminalStatus status,
        ConnectionBinding? expected=null)
    {
        Session? session=null;
        lock(_gate)
        {
            if(!_byActor.TryGetValue(actorId,out long tradeId)||
               !_sessions.TryGetValue(tradeId,out var active)||active.CommitStarted)return;
            var self=active.Find(actorId);
            if(self is null||(expected.HasValue&&self.Binding!=expected.Value))return;
            active.Revision++;
            session=active;
            RemoveLocked(active);
        }
        QueueTerminalBoth(session!,router,status);
    }

    private void CancelSession(
        Session session,WorldResultRouter router,TradeTerminalStatus status)
    {
        lock(_gate)
        {
            if(!_sessions.TryGetValue(session.TradeId,out var active)||
               !ReferenceEquals(active,session)||session.CommitStarted)return;
            session.Revision++;
            RemoveLocked(session);
        }
        QueueTerminalBoth(session,router,status);
    }

    private void RemoveLocked(Session session)
    {
        _sessions.Remove(session.TradeId);
        _byActor.Remove(session.First.Binding.ActorId);
        _byActor.Remove(session.Second.Binding.ActorId);
    }

    private void QueueBoth(Session session,WorldResultRouter router)
    {
        lock(_gate)
        {
            // Serialize state mutation and reliable FIFO admission together.
            // Otherwise a concurrent cancel/disconnect can enqueue a terminal
            // after the state change but before this older snapshot reaches
            // the per-connection FIFO, producing terminal -> stale snapshot.
            var first=BuildSnapshot(session.First,session);
            var second=BuildSnapshot(session.Second,session);
            router.QueueTradeSnapshot(session.First.Binding,first);
            router.QueueTradeSnapshot(session.Second.Binding,second);
        }
    }

    private static TradeSnapshotWire BuildSnapshot(
        Participant self,Session session)
    {
        var other=session.Other(self.Binding.ActorId);
        TradeSessionWireState state;
        if(!session.Accepted)
            state=self==session.First
                ? TradeSessionWireState.InviteSent:TradeSessionWireState.InviteReceived;
        else
            state=session.First.Locked&&session.Second.Locked
                ? TradeSessionWireState.AwaitingFinalConfirm:TradeSessionWireState.Active;
        return new(
            self.Binding.MapEpoch,session.TradeId,session.Revision,state,
            other.Binding.ActorId,self.Money,other.Money,
            self.Locked,other.Locked,self.Confirmed,other.Confirmed,
            self.Items.Values.OrderBy(x=>x.ItemUid).ToArray(),
            other.Items.Values.OrderBy(x=>x.ItemUid).ToArray());
    }

    private void QueueTerminalBoth(
        Session session,WorldResultRouter router,TradeTerminalStatus status)
    {
        QueueTerminal(session.First,session,router,status,0);
        QueueTerminal(session.Second,session,router,status,0);
    }

    private static void QueueTerminal(
        Participant self,Session session,WorldResultRouter router,
        TradeTerminalStatus status,long wallet)
    {
        router.QueueTradeTerminal(self.Binding,new(
            self.Binding.MapEpoch,session.TradeId,session.Revision,status,
            status==TradeTerminalStatus.Success?wallet:0));
    }
}
