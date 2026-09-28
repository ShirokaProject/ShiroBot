# Telegram Model 特有能力

当前 `ShiroBot.Model.Telegram` 提供 `TelegramUser`：

| 字段 | 含义 |
| --- | --- |
| `UserId` | Telegram 用户 ID，类型为 `long` |
| `FirstName` / `LastName` | 名字与可选姓氏 |
| `Username` | 可选用户名 |
| `IsBot` | 是否为机器人 |
| `DisplayName` | 由名字与姓氏组成的显示名 |

当前没有 Telegram 专有扩展接口。普通消息、事件、用户和会话操作使用 [SDK 通用 API](/plugin/apis) 与[通用 Model](/plugin/models)；平台原始对象可由适配器放入 `BotEvent.Raw` 或 `RawSegment.Payload`。
