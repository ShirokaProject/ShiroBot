namespace ShiroBot.SDK.Models;

/// <summary>用户身份只在来源实例内有效。跨实例权限共享必须显式配置。</summary>
public sealed record UserReference(string InstanceId, string UserId)
{
    public bool Matches(UserReference other) =>
        string.Equals(InstanceId, other.InstanceId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(UserId, other.UserId, StringComparison.Ordinal);

    public string ToConfigString() => Uri.EscapeDataString(InstanceId) + ":" + Uri.EscapeDataString(UserId);

    public static UserReference Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1 || value.IndexOf(':', separator + 1) >= 0)
            throw new FormatException("Permission entries must be instanceId:userId; escape ':' as %3A. Bare user IDs are not accepted.");
        var result = new UserReference(Uri.UnescapeDataString(value[..separator]), Uri.UnescapeDataString(value[(separator + 1)..]));
        ArgumentException.ThrowIfNullOrWhiteSpace(result.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.UserId);
        return result;
    }
}

/// <summary>会话身份；显示名称不参与身份匹配。</summary>
public sealed record ChannelReference(string InstanceId, Channel Channel)
{
    public bool Matches(ChannelReference other) =>
        string.Equals(InstanceId, other.InstanceId, StringComparison.OrdinalIgnoreCase) &&
        Channel.Type == other.Channel.Type && Channel.Id == other.Channel.Id &&
        (Channel.GuildId ?? (Channel.Type == ChannelType.Group ? Channel.Id : null)) ==
        (other.Channel.GuildId ?? (other.Channel.Type == ChannelType.Group ? other.Channel.Id : null));
}

/// <summary>消息 ID 仅在来源实例和会话内唯一。</summary>
public sealed record MessageReference(string InstanceId, Channel Channel, string MessageId)
{
    public bool Matches(MessageReference other) =>
        MessageId == other.MessageId && new ChannelReference(InstanceId, Channel).Matches(new(other.InstanceId, other.Channel));
}
