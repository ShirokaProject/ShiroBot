using System.Text.Json;
using ShiroBot.SDK.Models;

namespace ShiroBot.Model.Discord;

public enum DiscordInteractionKind
{
    Ping = 1,

    /// <summary>斜杠命令、用户命令或消息命令。</summary>
    ApplicationCommand = 2,

    /// <summary>按钮或下拉菜单。</summary>
    MessageComponent = 3,

    /// <summary>命令选项的自动补全。用 <see cref="IDiscordInteractionApi.AutocompleteAsync"/> 回答。</summary>
    ApplicationCommandAutocomplete = 4,

    /// <summary>提交弹窗。</summary>
    ModalSubmit = 5
}

public enum DiscordCommandType
{
    /// <summary>斜杠命令。</summary>
    ChatInput = 1,

    /// <summary>右键用户菜单。</summary>
    User = 2,

    /// <summary>右键消息菜单。</summary>
    Message = 3,

    /// <summary>应用启动入口。</summary>
    PrimaryEntryPoint = 4
}

public enum DiscordCommandOptionType
{
    SubCommand = 1,
    SubCommandGroup = 2,
    String = 3,
    Integer = 4,
    Boolean = 5,
    User = 6,
    Channel = 7,
    Role = 8,
    Mentionable = 9,
    Number = 10,
    Attachment = 11
}

/// <summary>交互中收到的命令选项。子命令和子命令组的参数在 <see cref="Options"/> 中。</summary>
public sealed record DiscordCommandOptionValue(string Name, DiscordCommandOptionType Type)
{
    /// <summary>原始取值：字符串、数字、布尔或 Snowflake 字符串。</summary>
    public JsonElement Value { get; init; }

    /// <summary>自动补全中当前正在输入的选项。</summary>
    public bool Focused { get; init; }

    public IReadOnlyList<DiscordCommandOptionValue> Options { get; init; } = [];

    public string? AsString() => Value.ValueKind == JsonValueKind.String ? Value.GetString() : Value.ValueKind == JsonValueKind.Undefined ? null : Value.ToString();
    public long? AsInteger() => Value.ValueKind == JsonValueKind.Number ? Value.GetInt64() : null;
    public double? AsNumber() => Value.ValueKind == JsonValueKind.Number ? Value.GetDouble() : null;
    public bool? AsBoolean() => Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? Value.GetBoolean() : null;
}

/// <summary>
///     Discord 交互，放在 SDK <see cref="InteractionEvent.Raw"/>。按钮回调仍映射到 <c>ButtonId</c>/<c>Data</c>；
///     斜杠命令、下拉菜单、弹窗和自动补全的内容在此读取。
/// </summary>
public sealed record DiscordInteraction
{
    public required string InteractionId { get; init; }
    public required DiscordInteractionKind Kind { get; init; }
    public ulong ApplicationId { get; init; }
    public ulong? GuildId { get; init; }
    public ulong? ChannelId { get; init; }
    public DiscordUser? User { get; init; }

    /// <summary>服务器中的发起成员。</summary>
    public DiscordMember? Member { get; init; }

    /// <summary>命令名；子命令见 <see cref="Options"/>。</summary>
    public string? CommandName { get; init; }

    public ulong? CommandId { get; init; }
    public DiscordCommandType? CommandType { get; init; }
    public IReadOnlyList<DiscordCommandOptionValue> Options { get; init; } = [];

    /// <summary>用户命令或消息命令的目标 ID。</summary>
    public ulong? TargetId { get; init; }

    /// <summary>组件或弹窗的 custom_id。</summary>
    public string? CustomId { get; init; }

    /// <summary>下拉菜单的选择值（文本值或 ID）。</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>弹窗提交的输入：custom_id → 值。</summary>
    public IReadOnlyDictionary<string, string> ModalValues { get; init; } = new Dictionary<string, string>();

    /// <summary>组件所在消息的 ID。</summary>
    public string? MessageId { get; init; }

    /// <summary>用户客户端语言。</summary>
    public string? Locale { get; init; }

    public string? GuildLocale { get; init; }

    /// <summary>机器人在该频道的权限。</summary>
    public DiscordPermissions AppPermissions { get; init; }

    /// <summary>Discord API 原始 Interaction（不含交互令牌）。</summary>
    public JsonElement Json { get; init; }
}

/// <summary>命令选项的固定选项。</summary>
/// <param name="Value">字符串、整数或数字。</param>
public sealed record DiscordCommandChoice(string Name, object Value)
{
    /// <summary>本地化名称：语言代码 → 名称。</summary>
    public IReadOnlyDictionary<string, string>? NameLocalizations { get; init; }
}

/// <summary>命令选项定义。必填选项必须排在可选选项之前。</summary>
public sealed record DiscordCommandOption(DiscordCommandOptionType Type, string Name, string Description)
{
    public bool Required { get; init; }
    public IReadOnlyList<DiscordCommandChoice> Choices { get; init; } = [];

    /// <summary>子命令和子命令组的选项。</summary>
    public IReadOnlyList<DiscordCommandOption> Options { get; init; } = [];

    public IReadOnlyList<DiscordChannelType>? ChannelTypes { get; init; }
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public int? MinLength { get; init; }
    public int? MaxLength { get; init; }

    /// <summary>启用自动补全；不能与 <see cref="Choices"/> 同时使用。</summary>
    public bool Autocomplete { get; init; }

    public IReadOnlyDictionary<string, string>? NameLocalizations { get; init; }
    public IReadOnlyDictionary<string, string>? DescriptionLocalizations { get; init; }
}

[Flags]
public enum DiscordCommandContexts
{
    None = 0,
    Guild = 1 << 0,

    /// <summary>与机器人的私聊。</summary>
    BotDirectMessage = 1 << 1,

    /// <summary>用户安装的应用在其他私聊和群聊中使用。</summary>
    PrivateChannel = 1 << 2
}

/// <summary>应用命令定义。名称：斜杠命令为 1–32 个小写字符，用户/消息命令可含空格和大写。</summary>
public sealed record DiscordApplicationCommand(string Name)
{
    public DiscordCommandType Type { get; init; } = DiscordCommandType.ChatInput;

    /// <summary>斜杠命令必填，1–100 个字符；用户和消息命令留空。</summary>
    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<DiscordCommandOption> Options { get; init; } = [];

    /// <summary>默认需要的成员权限；null 表示所有人可用，<see cref="DiscordPermissions.None"/> 表示仅管理员。</summary>
    public DiscordPermissions? DefaultMemberPermissions { get; init; }

    /// <summary>可使用的场景；null 使用 Discord 默认值。</summary>
    public DiscordCommandContexts? Contexts { get; init; }

    public bool Nsfw { get; init; }
    public IReadOnlyDictionary<string, string>? NameLocalizations { get; init; }
    public IReadOnlyDictionary<string, string>? DescriptionLocalizations { get; init; }
}

/// <summary>已注册的命令。</summary>
public sealed record DiscordRegisteredCommand
{
    public required ulong CommandId { get; init; }
    public required string Name { get; init; }
    public DiscordCommandType Type { get; init; }
    public string Description { get; init; } = string.Empty;

    /// <summary>服务器命令所属服务器；全局命令为 null。</summary>
    public ulong? GuildId { get; init; }

    public ulong Version { get; init; }
}

/// <summary>弹窗，最多 5 个输入框。</summary>
public sealed record DiscordModal(string CustomId, string Title, IReadOnlyList<DiscordTextInput> Inputs);
