using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Hotkey;

public sealed class HotkeyCoordinator
{
    private readonly SqliteIdentityStore _identity;

    public HotkeyCoordinator(SqliteIdentityStore identity)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
    }

    public async Task HandleAsync(TrustedHotkeyCommand request,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        if(!router.IsCurrentBinding(binding))return;

        HotkeyResultStatus status=HotkeyResultStatus.Applied;
        if(request.Action==HotkeyCommandAction.Save)
        {
            bool owned=request.DataType switch
            {
                HotkeyDataType.Item => (await _identity.ListInventoryItemsAsync(
                    request.ActorId).ConfigureAwait(false))
                    .Any(x=>x.ItemUid==request.DataId&&x.Quantity>0),
                HotkeyDataType.Skill => (await _identity.ListCharacterSkillsAsync(
                    request.ActorId).ConfigureAwait(false))
                    .Any(x=>x.SkillId==request.DataId&&x.Level>0),
                _ => false
            };
            if(owned)
                await _identity.SaveCharacterHotkeyAsync(
                    request.ActorId,request.Slot,(int)request.DataType,request.DataId)
                    .ConfigureAwait(false);
            else
                status=HotkeyResultStatus.InvalidBinding;
        }
        else if(request.Action==HotkeyCommandAction.Remove)
            await _identity.RemoveCharacterHotkeyAsync(
                request.ActorId,request.Slot).ConfigureAwait(false);
        else if(request.Action==HotkeyCommandAction.Swap)
        {
            int destination=checked((int)request.DataId-1);
            if(!await _identity.SwapCharacterHotkeysAsync(
                    request.ActorId,request.Slot,destination).ConfigureAwait(false))
                status=HotkeyResultStatus.InvalidBinding;
        }

        await QueueSnapshotAsync(binding,request.HotkeySequence,status,router)
            .ConfigureAwait(false);
    }

    private async Task QueueSnapshotAsync(
        ConnectionBinding binding,uint sequence,HotkeyResultStatus status,
        WorldResultRouter router)
    {
        var rows=await _identity.ListCharacterHotkeysAsync(binding.ActorId)
            .ConfigureAwait(false);
        var entries=rows.Select(x=>new HotkeyEntryWire(
            checked((byte)x.Slot),(HotkeyDataType)x.DataType,x.DataId)).ToArray();
        router.QueueHotkeySnapshot(binding,new(
            binding.MapEpoch,sequence,status,entries));
    }
}
