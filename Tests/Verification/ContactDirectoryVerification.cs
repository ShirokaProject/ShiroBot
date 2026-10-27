using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;

internal static class ContactDirectoryVerification
{
    public static async Task RunAsync()
    {
        var friends = new ListingUserService { Friends = [new User("10001") { Name = "小白" }] };
        var listing = new VerificationAdapter("qq") { User = friends, Channel = new ListingChannelService() };
        var silent = new VerificationAdapter("qq-official");
        var bot = new BotContext(null, [], [], new WebHostContext("http://127.0.0.1", false));
        bot.RegisterAdapter(listing, "qq-main", "QQ 主号");
        bot.RegisterAdapter(silent, "official");
        var directory = new ContactDirectory(bot);

        var users = await directory.GetAsync(DirectoryKind.Users, refresh: false, CancellationToken.None);
        if (users.Entries is not [{ Instance: "qq-main", InstanceName: "QQ 主号", Id: "10001", Name: "小白" }] ||
            users.Unavailable is not [{ Instance: "official", Reason: "不支持读取好友列表" }])
            throw new InvalidOperationException("Friend directory did not list per instance or report the unsupported adapter.");

        // Only group conversations are offered; direct and thread channels are not groups to route.
        var groups = await directory.GetAsync(DirectoryKind.Groups, refresh: false, CancellationToken.None);
        if (!groups.Entries.Select(entry => entry.Id).SequenceEqual(["913449089"]))
            throw new InvalidOperationException("Group directory listed non-group channels.");

        friends.Friends = [];
        if ((await directory.GetAsync(DirectoryKind.Users, refresh: false, CancellationToken.None)).Entries.Count != 1 ||
            (await directory.GetAsync(DirectoryKind.Users, refresh: true, CancellationToken.None)).Entries.Count != 0)
            throw new InvalidOperationException("Directory did not cache for a minute or refresh on request.");
        Console.WriteLine("Contact directory listing, unsupported adapters, caching and refresh verification passed.");
    }

    private sealed class ListingUserService : IUserService
    {
        public IReadOnlyList<User> Friends { get; set; } = [];
        public Task<IReadOnlyList<User>> GetFriendsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Friends);
    }

    private sealed class ListingChannelService : IChannelService
    {
        public Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Channel>>([Channel.Group("913449089") with { Name = "测试群" }, Channel.Direct("10001")]);
    }
}
