using SGTest.Server.Friend;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Pvp;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class FriendChecks
{
    public static void Verify()
    {
        Checks.Run("friend wire roundtrips invite accept reject remove refresh and presence",Wire);
        Checks.Run("friend gateway binds authenticated sender and rejects replay",GatewayAdmission);
        Checks.Run("friend persistence is bilateral durable and deletion is symmetric",
            ()=>Persistence().GetAwaiter().GetResult());
        Checks.Run("friend coordinator broadcasts online offline relog and removal",
            ()=>PresenceLifecycle().GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var add=new FriendCommandWire(7,1,FriendCommandAction.Add,0,"Buddy");
        var addBytes=WireProtocol.EncodeFriendCommand(add);
        Checks.Require(WireProtocol.TryDecodeFriendCommand(addBytes,out var parsed)&&parsed==add,
            "friend add-by-name wire roundtrip failed");
        Checks.Require(Convert.ToHexString(addBytes).ToLowerInvariant()==
            "240000000100780007000000000000000100000001000000000000000005004275646479",
            "friend add wire changed from Godot golden contract");

        var remove=new FriendCommandWire(7,2,FriendCommandAction.Remove,42);
        Checks.Require(WireProtocol.TryDecodeFriendCommand(
            WireProtocol.EncodeFriendCommand(remove),out parsed)&&parsed==remove,
            "friend remove wire roundtrip failed");
        var refresh=new FriendCommandWire(7,3,FriendCommandAction.Refresh,0);
        Checks.Require(WireProtocol.TryDecodeFriendCommand(
            WireProtocol.EncodeFriendCommand(refresh),out parsed)&&parsed==refresh,
            "friend refresh wire roundtrip failed");
        var accept=new FriendCommandWire(7,4,FriendCommandAction.Accept,42);
        Checks.Require(WireProtocol.TryDecodeFriendCommand(
            WireProtocol.EncodeFriendCommand(accept),out parsed)&&parsed==accept,
            "friend accept wire roundtrip failed");
        var reject=new FriendCommandWire(7,5,FriendCommandAction.Reject,42);
        Checks.Require(WireProtocol.TryDecodeFriendCommand(
            WireProtocol.EncodeFriendCommand(reject),out parsed)&&parsed==reject,
            "friend reject wire roundtrip failed");

        var result=new FriendResultWire(
            7,2,FriendCommandAction.Remove,FriendResultStatus.Success,42);
        Checks.Require(WireProtocol.TryDecodeFriendResult(
            WireProtocol.EncodeFriendResult(result),out var decodedResult)&&decodedResult==result,
            "friend result roundtrip failed");

        var snapshot=new FriendSnapshotWire(7,[
            new FriendEntryWire(42,"Buddy",true),
            new FriendEntryWire(43,"Offline",false)
        ]);
        Checks.Require(WireProtocol.TryDecodeFriendSnapshot(
            WireProtocol.EncodeFriendSnapshot(snapshot),out var decoded)&&decoded is not null&&
            decoded.Friends.Count==2&&decoded.Friends[0].IsOnline&&
            !decoded.Friends[1].IsOnline,
            "friend presence snapshot roundtrip failed");

        var invite=new FriendInviteWire(7,42,9,FriendInviteState.Request,"Buddy");
        var inviteBytes=WireProtocol.EncodeFriendInvite(invite);
        Checks.Require(WireProtocol.TryDecodeFriendInvite(inviteBytes,out var decodedInvite)&&
            decodedInvite==invite,"friend invite roundtrip failed");
        Checks.Require(Convert.ToHexString(inviteBytes).ToLowerInvariant()==
            "280000000100880007000000000000002a0000000000000009000000000000000005004275646479",
            "friend invite wire changed from Godot golden contract");

        var malformed=WireProtocol.EncodeFriendCommand(add);
        malformed[20]=9;
        Checks.Require(!WireProtocol.TryDecodeFriendCommand(malformed,out _),
            "friend wire accepted invalid action");
    }

    private static void GatewayAdmission()
    {
        var gateway=Gateway(Actor(11,9,3),"FriendSender");
        var frame=WireProtocol.EncodeFriendCommand(
            new FriendCommandWire(3,7,FriendCommandAction.Add,0,"FriendTarget"));
        TrustedFriendCommand trusted=default;
        Checks.Require(gateway.FeedClientBytes(
                frame,_=>true,out int accepted,
                postFriend:value=>{trusted=value;return true;})==
                WireFeedStatus.Accepted&&accepted==1&&
            trusted.ActorId==11&&trusted.ConnectionEpoch==9&&
            trusted.RequestMapEpoch==3&&trusted.FriendSequence==7&&
            trusted.Action==FriendCommandAction.Add&&
            trusted.TargetActorId==0&&trusted.TargetName=="FriendTarget",
            "friend Gateway did not bind authenticated sender/sequence");
        Checks.Require(gateway.FeedClientBytes(
                frame,_=>true,out accepted,
                postFriend:_=>true)==WireFeedStatus.ProtocolViolation,
            "friend Gateway accepted replayed sequence");
    }

    private static async Task Persistence()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_friend_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            long a,b;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "friend_a","Friend-Account-Secret-2026!")).AccountId;
                long other=(await store.CreateAccountAsync(
                    "friend_b","Friend-Other-Secret-2026!")).AccountId;
                a=(await store.CreateCharacterAsync(
                    account,0,"FriendAlpha",new(15,100,100))).Character!.CharacterId;
                b=(await store.CreateCharacterAsync(
                    other,0,"FriendBeta",new(15,110,100))).Character!.CharacterId;

                var added=await store.AddFriendAsync(a,0,"FriendBeta");
                Checks.Require(added.Status==FriendStoreStatus.Success&&
                    added.TargetCharacterId==b,"friend add-by-name failed");
                var fromA=await store.GetFriendsAsync(a);
                var fromB=await store.GetFriendsAsync(b);
                Checks.Require(fromA.Count==1&&fromA[0].CharacterId==b&&
                    fromB.Count==1&&fromB[0].CharacterId==a,
                    "friend relation was not bilateral");
                Checks.Require((await store.AddFriendAsync(a,b)).Status==
                    FriendStoreStatus.AlreadyFriends,
                    "duplicate friend relation was not rejected");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var persisted=await reopened.GetFriendsAsync(a);
                Checks.Require(persisted.Count==1&&persisted[0].CharacterId==b,
                    "friend relation did not survive reopen");
                Checks.Require(await reopened.RemoveFriendAsync(a,b)==
                    FriendStoreStatus.Success,"friend remove failed");
                Checks.Require((await reopened.GetFriendsAsync(a)).Count==0&&
                    (await reopened.GetFriendsAsync(b)).Count==0,
                    "friend remove was not symmetric");
            }
        }
        finally
        {
            try{Directory.Delete(root,true);}catch{}
        }
    }

    private static async Task PresenceLifecycle()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_friend_presence_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long accountA=(await store.CreateAccountAsync(
                "friend_presence_a","Friend-Presence-A-Secret-2026!")).AccountId;
            long accountB=(await store.CreateAccountAsync(
                "friend_presence_b","Friend-Presence-B-Secret-2026!")).AccountId;
            long aId=(await store.CreateCharacterAsync(
                accountA,0,"PresenceAlpha",new(15,100,100))).Character!.CharacterId;
            long bId=(await store.CreateCharacterAsync(
                accountB,0,"PresenceBeta",new(15,110,100))).Character!.CharacterId;

            var router=new WorldResultRouter();
            var coordinator=new FriendCoordinator(store);
            var a=Gateway(Actor(aId,11,2),"PresenceAlpha");
            var b=Gateway(Actor(bId,22,5),"PresenceBeta");
            Checks.Require(router.TryAttach(a)&&router.TryAttach(b),
                "friend presence gateways attach failed");
            await coordinator.AttachAsync(a.Binding,router);
            await coordinator.AttachAsync(b.Binding,router);
            Checks.Require(!a.TryDequeueReliable(out _)&&!b.TryDequeueReliable(out _),
                "friend attach emitted unnecessary empty pre-WorldReady snapshot");

            FeedFriend(a,new FriendCommandWire(
                2,1,FriendCommandAction.Add,0,"PresenceBeta"),coordinator,router);
            var addResult=DrainResult(a);
            var invite=DrainInvite(b);
            Checks.Require(addResult.Status==FriendResultStatus.Success&&
                addResult.TargetActorId==bId&&
                invite.State==FriendInviteState.Request&&invite.ActorId==aId&&
                invite.ActorConnectionEpoch==11&&invite.Name=="PresenceAlpha"&&
                (await store.GetFriendsAsync(aId)).Count==0&&
                (await store.GetFriendsAsync(bId)).Count==0,
                "friend invite was not delivered before persistence");

            FeedFriend(b,new FriendCommandWire(
                5,1,FriendCommandAction.Accept,aId),coordinator,router);
            var acceptResult=DrainResult(b);
            var accepted=DrainInvite(a);
            var bAdded=DrainSnapshot(b);
            var aAdded=DrainSnapshot(a);
            Checks.Require(acceptResult.Status==FriendResultStatus.Success&&
                acceptResult.TargetActorId==aId&&
                accepted.State==FriendInviteState.Accepted&&accepted.ActorId==bId&&
                accepted.Name=="PresenceBeta"&&
                aAdded.Friends.Count==1&&aAdded.Friends[0].CharacterId==bId&&
                aAdded.Friends[0].IsOnline&&
                bAdded.Friends.Count==1&&bAdded.Friends[0].CharacterId==aId&&
                bAdded.Friends[0].IsOnline,
                "friend accept did not persist and broadcast bilateral presence");

            await coordinator.DetachAsync(b.Binding,router);
            var offline=DrainSnapshot(a);
            Checks.Require(offline.Friends.Count==1&&
                offline.Friends[0].CharacterId==bId&&!offline.Friends[0].IsOnline,
                "friend disconnect did not notify peer offline");
            Checks.Require(router.TryDetach(b),"friend old binding detach failed");

            var b2=Gateway(Actor(bId,23,6),"PresenceBeta");
            Checks.Require(router.TryAttach(b2),"friend relog binding attach failed");
            await coordinator.AttachAsync(b2.Binding,router);
            var bRelog=DrainSnapshot(b2);
            var aRelog=DrainSnapshot(a);
            Checks.Require(bRelog.Friends.Count==1&&bRelog.Friends[0].IsOnline&&
                aRelog.Friends.Count==1&&aRelog.Friends[0].IsOnline,
                "friend relog did not restore live presence");

            FeedFriend(b2,new FriendCommandWire(
                6,1,FriendCommandAction.Remove,aId),coordinator,router);
            Checks.Require(DrainResult(b2).Status==FriendResultStatus.Success&&
                DrainSnapshot(b2).Friends.Count==0&&
                DrainSnapshot(a).Friends.Count==0,
                "friend remove did not broadcast bilateral clear");
            Checks.Require((await store.GetFriendsAsync(aId)).Count==0&&
                (await store.GetFriendsAsync(bId)).Count==0,
                "friend coordinator remove did not persist symmetric deletion");
        }
        finally
        {
            try{Directory.Delete(root,true);}catch{}
        }
    }

    private static void FeedFriend(
        GatewayConnection gateway,FriendCommandWire command,
        FriendCoordinator coordinator,WorldResultRouter router)
    {
        var status=gateway.FeedClientBytes(
            WireProtocol.EncodeFriendCommand(command),_=>true,out int accepted,
            postFriend:value=>
            {
                coordinator.HandleAsync(value,router).GetAwaiter().GetResult();
                return true;
            });
        Checks.Require(status==WireFeedStatus.Accepted&&accepted==1,
            "friend encoded client frame was not admitted through Gateway");
    }

    private static GatewayConnection Gateway(ActorSnapshot actor,string name)
    {
        var pvp=new CharacterPvpState(
            actor.ActorId,PvpMode.ProtectedBlue,false,0,0,0);
        return new GatewayConnection(actor,displayName:name,pvpState:pvp);
    }

    private static ActorSnapshot Actor(long id,long connection,long mapEpoch)=>
        new(id,15,connection,mapEpoch,0,0,8,8,1,false,1,false);

    private static FriendResultWire DrainResult(GatewayConnection gateway)
    {
        if(!gateway.TryDequeueReliable(out var frame)||
           !WireProtocol.TryDecodeFriendResult(frame,out var value))
            throw new Exception("friend result missing");
        return value;
    }

    private static FriendInviteWire DrainInvite(GatewayConnection gateway)
    {
        if(!gateway.TryDequeueReliable(out var frame)||
           !WireProtocol.TryDecodeFriendInvite(frame,out var value))
            throw new Exception("friend invite missing");
        return value;
    }

    private static FriendSnapshotWire DrainSnapshot(GatewayConnection gateway)
    {
        if(!gateway.TryDequeueReliable(out var frame)||
           !WireProtocol.TryDecodeFriendSnapshot(frame,out var value)||
           value is null)
            throw new Exception("friend snapshot missing");
        return value;
    }
}
