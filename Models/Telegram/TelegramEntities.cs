namespace ShiroBot.Model.Telegram;

/// <summary>Telegram 用户的协议无关表示。</summary>
public sealed record TelegramUser
{
    public required long UserId { get; init; }
    public required string FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Username { get; init; }
    public bool IsBot { get; init; }

    public string DisplayName => string.Join(" ", new[] { FirstName, LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
