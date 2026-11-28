# 命令菜单与资料（ITelegramBotProfileApi）

命名空间：`ShiroBot.Model.Telegram`。仅机器人账号（BotFather 创建的机器人）实现；用户账号适配器上为 null。

机器人的命令菜单、名称与介绍，仅机器人。自身资料见 `ITelegramClientApi.GetMeAsync`。

```csharp
var api = Context.GetAdapterExtension<ITelegramBotProfileApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## SetCommandsAsync

设置命令菜单。

```csharp
Task SetCommandsAsync(
    IReadOnlyList<TelegramBotCommand> commands,
    TelegramCommandScope? scope = null,
    string? languageCode = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `commands` | `IReadOnlyList<TelegramBotCommand>` | `必传` | 命令列表。 |
| `scope` | `TelegramCommandScope?` | `null` | 命令生效范围；null 为默认范围。 |
| `languageCode` | `string?` | `null` | 两位 ISO 639-1 语言代码；null 表示所有未单独设置的语言。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetCommandsAsync

查询已设置的命令菜单。

```csharp
Task<IReadOnlyList<TelegramBotCommand>> GetCommandsAsync(
    TelegramCommandScope? scope = null,
    string? languageCode = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `scope` | `TelegramCommandScope?` | `null` | 命令生效范围；null 为默认范围。 |
| `languageCode` | `string?` | `null` | 两位 ISO 639-1 语言代码；null 表示所有未单独设置的语言。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<TelegramBotCommand>>`。异步返回 `IReadOnlyList<TelegramBotCommand>`。

## DeleteCommandsAsync

删除命令菜单，回退到更宽范围的设置。

```csharp
Task DeleteCommandsAsync(
    TelegramCommandScope? scope = null,
    string? languageCode = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `scope` | `TelegramCommandScope?` | `null` | 命令生效范围；null 为默认范围。 |
| `languageCode` | `string?` | `null` | 两位 ISO 639-1 语言代码；null 表示所有未单独设置的语言。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SetBotNameAsync

修改机器人显示名称；null 清除该语言的单独设置。

```csharp
Task SetBotNameAsync(
    string? name,
    string? languageCode = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `name` | `string?` | `必传` | 名称；null 表示不修改。 |
| `languageCode` | `string?` | `null` | 两位 ISO 639-1 语言代码；null 表示所有未单独设置的语言。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SetBotDescriptionAsync

空会话中显示的介绍，0–512 个字符。

```csharp
Task SetBotDescriptionAsync(
    string? description,
    string? languageCode = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `description` | `string?` | `必传` | 介绍文字；null 或空字符串清除。 |
| `languageCode` | `string?` | `null` | 两位 ISO 639-1 语言代码；null 表示所有未单独设置的语言。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SetBotShortDescriptionAsync

资料页和分享链接中显示的简介，0–120 个字符。

```csharp
Task SetBotShortDescriptionAsync(
    string? description,
    string? languageCode = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `description` | `string?` | `必传` | 介绍文字；null 或空字符串清除。 |
| `languageCode` | `string?` | `null` | 两位 ISO 639-1 语言代码；null 表示所有未单独设置的语言。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
