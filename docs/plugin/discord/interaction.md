# 交互与应用命令（IDiscordInteractionApi）

命名空间：`ShiroBot.Model.Discord`。仅机器人账号（Developer Portal 创建的机器人）实现；用户账号适配器上为 null。

交互回复与应用命令，仅机器人。交互令牌有效期 15 分钟，首次回复必须在 3 秒内发出。 适配器自动确认交互时（`AutoAcknowledges`），首次回复已用延迟回复占用，只能使用 `EditOriginalAsync` 和 `FollowupAsync`；弹窗、自动补全和临时可见的首次回复需要关闭自动确认。

```csharp
var api = Context.GetAdapterExtension<IDiscordInteractionApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## ApplicationId

应用 ID；READY 之后可用。

```csharp
string? ApplicationId { get; }
```

接口默认值：`null`；适配器按实际情况返回。

## AutoAcknowledges

属性。

```csharp
bool AutoAcknowledges { get; }
```

接口默认值：`true`；适配器按实际情况返回。

## RespondAsync

首次回复一条消息；`DiscordMessageFlags.Ephemeral` 使其只对发起者可见。

```csharp
Task RespondAsync(
    string interactionId,
    DiscordMessageContent content,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 交互 ID，即 SDK `InteractionEvent.InteractionId`。 |
| `content` | `DiscordMessageContent` | `必传` | 要发送的内容。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## DeferAsync

先确认，稍后用 `EditOriginalAsync` 给出结果。组件交互且 `updateMessage` 为 true 时 不显示“正在思考”，之后编辑的是组件所在的消息。

```csharp
Task DeferAsync(
    string interactionId,
    bool ephemeral = false,
    bool updateMessage = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 交互 ID，即 SDK `InteractionEvent.InteractionId`。 |
| `ephemeral` | `bool` | `false` | 只对发起者可见。 |
| `updateMessage` | `bool` | `false` | 组件交互时更新组件所在的消息，而不是发送新消息。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## UpdateMessageAsync

组件交互：直接修改组件所在的消息。

```csharp
Task UpdateMessageAsync(
    string interactionId,
    DiscordMessageContent content,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 交互 ID，即 SDK `InteractionEvent.InteractionId`。 |
| `content` | `DiscordMessageContent` | `必传` | 要发送的内容。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## ShowModalAsync

弹出表单；不能用于弹窗提交和自动补全交互。

```csharp
Task ShowModalAsync(
    string interactionId,
    DiscordModal modal,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 交互 ID，即 SDK `InteractionEvent.InteractionId`。 |
| `modal` | `DiscordModal` | `必传` | 弹窗定义。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## AutocompleteAsync

回答自动补全，最多 25 个选项。

```csharp
Task AutocompleteAsync(
    string interactionId,
    IReadOnlyList<DiscordCommandChoice> choices,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 交互 ID，即 SDK `InteractionEvent.InteractionId`。 |
| `choices` | `IReadOnlyList<DiscordCommandChoice>` | `必传` | 自动补全选项，最多 25 个。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## EditOriginalAsync

编辑首次回复（包括自动确认产生的延迟回复）。

```csharp
Task<DiscordSentMessage> EditOriginalAsync(
    string interactionId,
    DiscordMessageContent content,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 交互 ID，即 SDK `InteractionEvent.InteractionId`。 |
| `content` | `DiscordMessageContent` | `必传` | 要发送的内容。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordSentMessage>`。异步返回 `DiscordSentMessage`。

## DeleteOriginalAsync

删除首次回复。

```csharp
Task DeleteOriginalAsync(string interactionId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 交互 ID，即 SDK `InteractionEvent.InteractionId`。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## FollowupAsync

追加一条消息；首次回复之后的 15 分钟内可用。

```csharp
Task<DiscordSentMessage> FollowupAsync(
    string interactionId,
    DiscordMessageContent content,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 交互 ID，即 SDK `InteractionEvent.InteractionId`。 |
| `content` | `DiscordMessageContent` | `必传` | 要发送的内容。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordSentMessage>`。异步返回 `DiscordSentMessage`。

## GetCommandsAsync

已注册的全局命令，或 `guildId` 服务器的命令。

```csharp
Task<IReadOnlyList<DiscordRegisteredCommand>> GetCommandsAsync(
    string? guildId = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string?` | `null` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordRegisteredCommand>>`。异步返回 `IReadOnlyList<DiscordRegisteredCommand>`。

## RegisterCommandsAsync

用给定列表整体替换已注册的命令（不在列表中的命令会被删除）。全局命令可能需要几分钟同步到客户端。

```csharp
Task<IReadOnlyList<DiscordRegisteredCommand>> RegisterCommandsAsync(
    IReadOnlyList<DiscordApplicationCommand> commands,
    string? guildId = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `commands` | `IReadOnlyList<DiscordApplicationCommand>` | `必传` | 命令列表。 |
| `guildId` | `string?` | `null` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordRegisteredCommand>>`。异步返回 `IReadOnlyList<DiscordRegisteredCommand>`。

## UpsertCommandAsync

新增或更新同名命令，不影响其他命令。

```csharp
Task<DiscordRegisteredCommand> UpsertCommandAsync(
    DiscordApplicationCommand command,
    string? guildId = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `command` | `DiscordApplicationCommand` | `必传` | 命令定义。 |
| `guildId` | `string?` | `null` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordRegisteredCommand>`。异步返回 `DiscordRegisteredCommand`。

## DeleteCommandAsync

删除一个已注册的命令。

```csharp
Task DeleteCommandAsync(
    string commandId,
    string? guildId = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `commandId` | `string` | `必传` | 已注册命令的 ID。 |
| `guildId` | `string?` | `null` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
