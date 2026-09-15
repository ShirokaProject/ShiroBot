namespace ShiroBot.Model.Discord;

/// <summary>Discord 用户的协议无关表示。</summary>
public sealed record DiscordUser
{
    public required ulong UserId { get; init; }
    public required string Username { get; init; }
    public string? GlobalName { get; init; }
    public string? Nickname { get; init; }
    public bool IsBot { get; init; }

    public string DisplayName => Nickname ?? GlobalName ?? Username;
}
