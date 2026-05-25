using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting.Context;

internal sealed class BotContext
{
    private IReadOnlyList<long> _ownerList;
    private IReadOnlyList<long> _adminList;

    public BotContext(IBotAdapter adapter, IReadOnlyList<long> ownerList, IReadOnlyList<long> adminList)
    {
        File = new FileContext(adapter.File);
        Friend = new FriendContext(adapter.Friend);
        Group = new GroupContext(adapter.Group);
        Message = new MessageContext(adapter.Message);
        System = new SystemContext(adapter.System);
        Updater = new UpdaterContext();
        _ownerList = ownerList;
        _adminList = adminList;
    }

    public IFileContext File { get; }
    public IFriendContext Friend { get; }
    public IGroupContext Group { get; }
    public IMessageContext Message { get; }
    public ISystemContext System { get; }
    public IUpdater Updater { get; }

    public IReadOnlyList<long> OwnerList => Volatile.Read(ref _ownerList);
    public IReadOnlyList<long> AdminList => Volatile.Read(ref _adminList);

    public void UpdateOwnerList(IReadOnlyList<long> ownerList)
    {
        Volatile.Write(ref _ownerList, ownerList);
    }

    public void UpdateAdminList(IReadOnlyList<long> adminList)
    {
        Volatile.Write(ref _adminList, adminList);
    }
}
