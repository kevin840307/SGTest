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
        IReadOnlyDictionary<int,TradeItemRule> itemRules)
    {
        _store=store??throw new ArgumentNullException(nameof(store));
        _world=world??throw new ArgumentNullException(nameof(world));
        _itemRules=itemRules??throw new ArgumentNullException(nameof(itemRules));
        if(itemRules.Count>100000)throw new ArgumentOutOfRangeException(nameof(itemRules));
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
        if(target is not { } peer||peer.MapId!=requester.MapId)return;
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
                item.ItemUid,item.ItemDefinitionId,item.Quantity,item.SourceDropId));
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

        var result=await _store.CommitInventoryTradeAsync(commit,_itemRules)
            .ConfigureAwait(false);
        if(result.Status is TradeCommitStatus.Applied or TradeCommitStatus.AlreadyApplied)
        {
            lock(_gate)
            {
                if(_sessions.TryGetValue(session!.TradeId,out var active)&&
                   ReferenceEquals(active,session))RemoveLocked(session);
            }
            QueueTerminal(
                session!.First,session,router,TradeTerminalStatus.Success,
                result.FirstWalletMoney);
            QueueTerminal(
                session.Second,session,router,TradeTerminalStatus.Success,
                result.SecondWalletMoney);
        }
        else
        {
            lock(_gate)
            {
                if(_sessions.TryGetValue(session!.TradeId,out var active)&&
                   ReferenceEquals(active,session))RemoveLocked(session);
            }
            QueueTerminalBoth(session!,router,TradeTerminalStatus.CommitFailed);
        }
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
        Session session,WorldResultRouter router)
    {
        var first=await _world.TrustedGetActorAsync(session.First.Binding.ActorId)
            .ConfigureAwait(false);
        var second=await _world.TrustedGetActorAsync(session.Second.Binding.ActorId)
            .ConfigureAwait(false);
        return first is { } a&&second is { } b&&
            ConnectionBinding.FromTrustedSnapshot(a)==session.First.Binding&&
            ConnectionBinding.FromTrustedSnapshot(b)==session.Second.Binding&&
            a.MapId==b.MapId&&router.IsCurrentBinding(session.First.Binding)&&
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
