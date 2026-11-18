namespace ShiroBot.Model.Telegram;

/// <summary>适配器登录的账号类型。机器人与用户账号共用本 Model，但可用的扩展接口不同。</summary>
public enum TelegramAccountKind
{
    /// <summary>BotFather 创建的机器人。</summary>
    Bot,

    /// <summary>用户自己的账号（MTProto 登录）。</summary>
    User
}

/// <summary>适配器与 Telegram 通信的方式。</summary>
public enum TelegramTransport
{
    /// <summary>HTTP Bot API（官方服务器或自建 Bot API Server）。</summary>
    BotApi,

    /// <summary>Telegram 原生协议 MTProto。</summary>
    Mtproto
}

/// <summary><see cref="TelegramMessage.Json"/> 与 <see cref="TelegramEventPayload.Json"/> 的结构。</summary>
public enum TelegramJsonFormat
{
    /// <summary>Bot API 对象（Message、Update 字段等），字段名与 Bot API 文档一致。</summary>
    BotApi,

    /// <summary>MTProto TL 对象的 JSON 表示，结构由适配器说明。</summary>
    Mtproto
}

/// <summary>MTProto peer 的类型。</summary>
public enum TelegramPeerKind
{
    User,

    /// <summary>普通群（chat）。</summary>
    BasicGroup,

    /// <summary>超级群组与频道（channel）。</summary>
    Channel
}

/// <summary>
///     会话 ID 规则：本 Model 和 SDK 频道 ID 一律使用 Bot API 格式——用户为正数，普通群为负数，
///     超级群组和频道为 <c>-100…</c>。MTProto 适配器在内部与 peer ID 互相转换，插件保存的 ID 在两种适配器之间通用。
/// </summary>
public static class TelegramChatIds
{
    private const long ChannelOffset = 1_000_000_000_000;

    /// <summary>MTProto peer 转为 Bot API 格式的会话 ID。</summary>
    public static long ToChatId(TelegramPeerKind kind, long peerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peerId);
        return kind switch
        {
            TelegramPeerKind.User => peerId,
            TelegramPeerKind.BasicGroup => -peerId,
            TelegramPeerKind.Channel => -(ChannelOffset + peerId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>Bot API 格式的会话 ID 转为 MTProto peer。</summary>
    public static (TelegramPeerKind Kind, long PeerId) ToPeer(long chatId) => chatId switch
    {
        > 0 => (TelegramPeerKind.User, chatId),
        0 => throw new ArgumentOutOfRangeException(nameof(chatId), "会话 ID 不能为 0。"),
        <= -ChannelOffset - 1 => (TelegramPeerKind.Channel, -chatId - ChannelOffset),
        _ => (TelegramPeerKind.BasicGroup, -chatId)
    };
}
