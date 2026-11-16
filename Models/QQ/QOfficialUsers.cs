namespace ShiroBot.Model.QQ;

/// <summary>QQ 官方用户基本信息查询，需要用户与机器人存在好友关系。</summary>
public interface IQOfficialUserApi
{
    Task<QOfficialUserInfo> GetUserInfoAsync(string userOpenId, CancellationToken cancellationToken = default);
}

/// <summary>UserOpenId 为请求时使用的 ID；UnionOpenId 仅在绑定第三方应用时有效。</summary>
public sealed record QOfficialUserInfo(string UserOpenId)
{
    public string? Username { get; init; }
    public string? AvatarUrl { get; init; }
    public string? UnionOpenId { get; init; }
    public bool AllowProactiveMessage { get; init; }
}
