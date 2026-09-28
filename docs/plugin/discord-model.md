# Discord Model 特有能力

当前 `ShiroBot.Model.Discord` 提供 `DiscordUser`，保存 Discord 用户的原生身份字段：

| 字段 | 含义 |
| --- | --- |
| `UserId` | Discord 用户 ID，类型为 `ulong` |
| `Username` | 用户名 |
| `GlobalName` | 全局显示名，可为空 |
| `Nickname` | 当前服务器昵称，可为空 |
| `IsBot` | 是否为机器人 |
| `DisplayName` | 优先取昵称，其次全局显示名，最后用户名 |

```csharp
using ShiroBot.Model.Discord;

if (evt.Platform == "discord" && evt.Raw is DiscordUser user)
    BotLog.Info(user.DisplayName);
```

`DiscordUser` 是数据模型，目前没有与 QQ `IQGroupApi` 类似的 Discord 专有扩展接口。收消息、发消息和管理频道仍使用 [SDK 通用 API](/plugin/apis) 与[通用 Model](/plugin/models)。是否在某个事件的 `Raw` 中收到该类型，取决于适配器映射。
