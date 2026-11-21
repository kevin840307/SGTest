using SGTest.Server.Gateway;
using SGTest.Server.Wire;

namespace SGTest.Server.PlayerShop;

public enum PlayerShopSequenceDecision : byte { New, ExactReplay, Stale }

public sealed class PlayerShopSequenceGate
{
    private long _mapEpoch;
    private uint _lastSequence;
    private TrustedPlayerShopCommand _last;
    private PlayerShopResultWire _receipt;

    public PlayerShopResultWire LastReceipt=>_receipt;

    public PlayerShopSequenceDecision Inspect(TrustedPlayerShopCommand request)
    {
        if(request.RequestMapEpoch<=0||request.ShopSequence==0)
            throw new ArgumentOutOfRangeException(nameof(request));
        if(request.RequestMapEpoch<_mapEpoch)return PlayerShopSequenceDecision.Stale;
        if(request.RequestMapEpoch>_mapEpoch)
        {
            _mapEpoch=request.RequestMapEpoch;
            _lastSequence=0;
            _last=default;
            _receipt=default;
        }
        if(request.ShopSequence>_lastSequence)return PlayerShopSequenceDecision.New;
        return request.ShopSequence==_lastSequence&&Same(_last,request)
            ?PlayerShopSequenceDecision.ExactReplay
            :PlayerShopSequenceDecision.Stale;
    }

    public void Record(TrustedPlayerShopCommand request,PlayerShopResultWire receipt)
    {
        if(Inspect(request)!=PlayerShopSequenceDecision.New||
           receipt.MapEpoch!=request.RequestMapEpoch||
           receipt.ShopSequence!=request.ShopSequence||
           receipt.Action!=request.Action)
            throw new InvalidOperationException("Player-shop receipt does not match admission");
        _lastSequence=request.ShopSequence;
        _last=request;
        _receipt=receipt;
    }

    private static bool Same(TrustedPlayerShopCommand a,TrustedPlayerShopCommand b)=>
        a.ActorId==b.ActorId&&a.ConnectionEpoch==b.ConnectionEpoch&&
        a.RequestMapEpoch==b.RequestMapEpoch&&a.ShopSequence==b.ShopSequence&&
        a.Action==b.Action&&a.SellerActorId==b.SellerActorId&&
        a.ListingId==b.ListingId&&a.ItemUid==b.ItemUid&&a.Price==b.Price;
}
