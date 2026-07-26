namespace ShiroBot.SDK.Models;

/// <summary>
/// 平台无关的用户。
/// </summary>
public sealed record User(string Id)
{
    /// <summary>用户昵称 / 显示名。</summary>
    public string? Name { get; init; }

    /// <summary>头像 URL，平台未提供时为 null。</summary>
    public string? AvatarUrl { get; init; }

    /// <summary>是否为机器人账号。</summary>
    public bool IsBot { get; init; }
}

/// <summary>
/// 成员在群 / 频道内的角色。
/// </summary>
public enum MemberRole
{
    Member,
    Admin,
    Owner
}

/// <summary>
/// 用户在特定群 / 频道内的成员信息。
/// </summary>
public sealed record Member(User User)
{
    /// <summary>群名片 / 服务器昵称，未设置时为 null。</summary>
    public string? Nick { get; init; }

    public MemberRole Role { get; init; } = MemberRole.Member;

    public DateTimeOffset? JoinedAt { get; init; }

    /// <summary>显示名：优先群名片，其次用户名，最后用户 ID。</summary>
    public string DisplayName => Nick ?? User.Name ?? User.Id;
}
