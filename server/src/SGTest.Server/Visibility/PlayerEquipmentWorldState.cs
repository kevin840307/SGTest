using SGTest.Server.Gateway;
using SGTest.Server.Identity;

namespace SGTest.Server.Visibility;

/// <summary>
/// Server-authoritative player equipment presentation attached to one actor
/// generation. Slot definitions come only from committed equipped_items; the
/// Client may map source-verified definitions to paper-doll art, but never
/// invents ownership or slot placement.
/// </summary>
public readonly record struct PlayerEquipmentWorldStateUpdate(
    long OwnerActorId,long OwnerConnectionEpoch,long OwnerMapEpoch,uint Revision,
    bool Present,
    int HeadDefinitionId,int LeftHandDefinitionId,int RightHandDefinitionId,
    int BodyDefinitionId,int LegDefinitionId,int WeaponDefinitionId,
    int ShieldDefinitionId,int BackDefinitionId,int CharmDefinitionId)
{
    public int DefinitionForSlot(int slotId) => slotId switch
    {
        1 => LeftHandDefinitionId,
        2 => RightHandDefinitionId,
        3 => BodyDefinitionId,
        4 => LegDefinitionId,
        5 => HeadDefinitionId,
        6 => WeaponDefinitionId,
        7 => ShieldDefinitionId,
        8 => BackDefinitionId,
        9 => CharmDefinitionId,
        _ => 0
    };

    public static PlayerEquipmentWorldStateUpdate FromEquipped(
        ConnectionBinding binding,uint revision,IReadOnlyList<EquippedItem> equipment)
    {
        ArgumentNullException.ThrowIfNull(equipment);
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0)
            throw new ArgumentOutOfRangeException(nameof(binding));
        Span<int> slots=stackalloc int[10];
        foreach(var item in equipment)
        {
            if(item.CharacterId!=binding.ActorId||item.SlotId is <1 or >9||
               item.ItemDefinitionId is <=0 or >100000000||slots[item.SlotId]!=0)
                throw new InvalidOperationException("Invalid committed equipment presentation state");
            slots[item.SlotId]=item.ItemDefinitionId;
        }
        return new(
            binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,revision,true,
            slots[5],slots[1],slots[2],slots[3],slots[4],slots[6],slots[7],slots[8],slots[9]);
    }

    public static PlayerEquipmentWorldStateUpdate Detached(ConnectionBinding binding) =>
        new(binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,0,false,
            0,0,0,0,0,0,0,0,0);
}
