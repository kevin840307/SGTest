using SGTest.Server.Gateway;
using System.Text;

namespace SGTest.Server.Visibility;

/// <summary>
/// Server-authoritative online player-shop presentation attached to one actor
/// generation. The durable item listings remain in SQLite; this state only
/// carries whether the live stall is visible and its source-backed display name.
/// </summary>
public readonly record struct PlayerShopWorldStateUpdate(
    long OwnerActorId,long OwnerConnectionEpoch,long OwnerMapEpoch,
    uint Revision,bool Present,string ShopName)
{
    public static PlayerShopWorldStateUpdate Open(
        ConnectionBinding binding,uint revision,string shopName)
    {
        string name=(shopName??string.Empty).Trim();
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0||
           revision==0||string.IsNullOrWhiteSpace(name)||
           name.EnumerateRunes().Count() is <1 or >20)
            throw new ArgumentOutOfRangeException(nameof(binding));
        return new(
            binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,
            revision,true,name);
    }

    public static PlayerShopWorldStateUpdate Closed(
        ConnectionBinding binding,uint revision)
    {
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0||
           revision==0)
            throw new ArgumentOutOfRangeException(nameof(binding));
        return new(
            binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,
            revision,false,string.Empty);
    }
}
