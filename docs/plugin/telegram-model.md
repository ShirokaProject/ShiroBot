# Telegram Model 特有能力

`ShiroBot.Model.Telegram` 补充通用 SDK 无法表达的 Telegram 数据与操作。插件先使用 `ShiroBot.SDK.Models`
处理普通消息，再按需探测 Telegram 扩展接口。Model 由宿主提供，插件不需要引用适配器程序集。

本文对应宿主与 SDK 1.0.4（Telegram Model ABI `1.1.0.0`）。只使用 `TelegramUser` 原有成员的组件可在更早的宿主上运行。

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "1.0.4")]
```

## 常用 Model 类型

| 名称 | 类型 | 必填字段 | 描述 |
| --- | --- | --- | --- |
| `TelegramUser` | 实体 | `UserId`、`FirstName` | 用户；`LanguageCode`、`IsPremium` 为可选资料 |
| `TelegramChat` | 实体 | `ChatId`、`Type` | 私聊、群、超级群组或频道；查询结果包含简介、默认权限等资料 |
| `TelegramChatMember` | 实体 | `User`、`Status` | 成员状态（群主、管理员、成员、受限、已离开、已封禁）、权限和期限 |
| `TelegramChatPermissions` / `TelegramAdminRights` | 值对象 | 无 | 成员权限与管理员权限 |
| `TelegramForumTopic` | 实体 | `ThreadId`、`Name` | 话题；SDK 频道 ID 为 `chatId:threadId` |
| `TelegramInviteLink` | 实体 | `Link` | 邀请链接，可要求审批 |
| `TelegramMessage` | 入站资料 | `MessageId`、`Chat` | 放在 `MessageEvent.Raw`：发送者会话、编辑时间、相册 ID、转发来源、原始 JSON |
| `TelegramOutgoingContent` | 出站内容基类 | 由子类型决定 | 文本、投票、位置、地点、联系人、骰子、贴纸、相册、发票 |
| `TelegramReplyMarkup` | 键盘基类 | 由子类型决定 | 内联键盘、自定义键盘、移除键盘、强制回复 |
| `TelegramInlineResult` | inline 结果基类 | `Id` | 文章、图片、贴纸 |
| `TelegramEventPayload` | 事件负载基类 | 由子类型决定 | 放在 `PlatformEvent.Raw`，均带 `UpdateId`、`Time` 和原始 `Json` |

ID 规则与 SDK 相同：会话参数接受数字 ID、`@username` 或 SDK 频道 ID，用户和消息 ID 使用 `User.Id`、`MessageId` 的字符串。
Model 实体中的原生 ID 为 `long`。

## 目前提供的功能

| 功能 | Model 接口 | 内容 |
| --- | --- | --- |
| 会话管理 | `ITelegramChatApi` | 会话资料、成员数、成员与管理员查询；封禁、解封、限制、提升、头衔、默认权限；标题、简介、头像；置顶；邀请链接；入群审批；话题的创建、修改、关闭、重开和删除；退出 |
| 原生消息 | `ITelegramMessageApi` | 发送原生内容；编辑文本、说明和键盘（包括 inline 消息）；转发、复制、批量删除；输入状态；结束投票；带提示回答按钮回调 |
| inline 模式 | `ITelegramInlineApi` | 回答 inline 查询 |
| 支付 | `ITelegramPaymentApi` | 付款前确认、收货方式、Telegram Stars 退款与余额；发送发票使用 `TelegramInvoiceContent` |
| 机器人资料 | `ITelegramBotProfileApi` | 自身资料、已见用户、命令菜单（按范围和语言）、名称与介绍 |
| 原始调用 | `ITelegramRawApi` | 调用任意 Bot API 方法并上传文件，接收原始 Update；只使用 System.Text.Json 类型 |

取得接口后，未实现的方法仍可能抛出 `NotSupportedException`。平台权限由 Telegram 校验，失败时抛出 `InvalidOperationException`，消息包含错误码与说明。

## 发送原生内容

```csharp
using ShiroBot.Model.Telegram;

var telegram = Context.GetAdapterExtension<ITelegramMessageApi>();
if (telegram is null) return;

await telegram.SendAsync(message.Channel,
    new TelegramPollContent("午饭吃什么？", ["面", "饭", "都行"]) { IsAnonymous = false },
    new TelegramSendOptions { ReplyToMessageId = message.MessageId });

await telegram.SendAsync(message.Channel, new TelegramTextContent("*已完成*") { ParseMode = TelegramParseMode.MarkdownV2 },
    new TelegramSendOptions
    {
        ReplyMarkup = new TelegramInlineKeyboard([
            [TelegramInlineButton.WithCallback("再来一次", "retry"), TelegramInlineButton.WithUrl("文档", "https://core.telegram.org/bots/api")]
        ])
    });

await telegram.SendChatActionAsync(message.Channel, TelegramChatAction.Typing);
```

相册使用 `TelegramMediaGroupContent`，每项的 `Uri` 可以是 file_id、HTTP(S) 地址或适配器支持的本地资源。
自定义键盘 `TelegramReplyKeyboard` 的按钮按下后发送按钮文本，作为普通消息到达。

内联按钮的 `CallbackData` 产生 SDK `InteractionEvent`，`Data` 为该值。适配器默认自动确认回调；需要显示提示或弹窗时，
关闭适配器的自动确认（`AutoAnswersCallbacks` 为 false），再调用 `AnswerCallbackAsync`。

## 入站内容

`MessageEvent.Raw` 为 `TelegramMessage`。没有通用段的内容以 `RawSegment` 上报，`Platform` 为 `telegram`：

| `RawSegment.Kind` | `Payload` |
| --- | --- |
| `TelegramSegmentKinds.Location` | `TelegramLocation` |
| `TelegramSegmentKinds.Venue` | `TelegramVenue` |
| `TelegramSegmentKinds.Contact` | `TelegramContact` |
| `TelegramSegmentKinds.Dice` | `TelegramDice` |
| `TelegramSegmentKinds.Poll` | `TelegramPoll` |
| `TelegramSegmentKinds.SuccessfulPayment` | `TelegramSuccessfulPayment` |
| `TelegramSegmentKinds.FormattedText` | `TelegramFormattedText`：带格式实体的文本，SDK 文本段只有纯文本 |
| 其他 Bot API 字段名 | `JsonElement` |

```csharp
foreach (var segment in message.Segments.OfType<RawSegment>())
    if (segment.Payload is TelegramLocation location)
        await Context.Message.ReplyAsync(message, $"{location.Latitude}, {location.Longitude}");
```

## 事件

Telegram 特有事件以 `PlatformEvent` 上报，`Kind` 为 `TelegramEventKinds` 中的常量：

| Kind | Raw | 说明 |
| --- | --- | --- |
| `MessageEdited` | `TelegramMessageEdited` | 消息或频道消息被编辑，内容已映射为 SDK 段 |
| `ChatMemberUpdated` / `MyChatMemberUpdated` | `TelegramChatMemberUpdated` | 成员或机器人自身的状态变化 |
| `ChatJoinRequest` | `TelegramChatJoinRequest` | 入群申请，用 `ITelegramChatApi` 审批 |
| `InlineQuery` / `ChosenInlineResult` | `TelegramInlineQuery` / `TelegramChosenInlineResult` | inline 模式 |
| `Poll` / `PollAnswer` | `TelegramPollUpdated` / `TelegramPollAnswer` | 投票状态与用户选择 |
| `PreCheckoutQuery` / `ShippingQuery` | `TelegramPreCheckoutQuery` / `TelegramShippingQuery` | 支付流程，需及时回答 |
| `ChatBoost` / `ChatBoostRemoved` | `TelegramChatBoost` | 助力变化 |

其他没有建模的 Update 以 Bot API 字段名为 `Kind`，`Raw` 为 `JsonElement`。消息、按钮回调、成员加入与离开、Reaction
仍映射为对应的 SDK 事件。

```csharp
Events.MapPlatform(TelegramEventKinds.InlineQuery, async evt =>
{
    if (evt.Raw is not TelegramInlineQuery query) return;
    var inline = Context.GetAdapterExtension<ITelegramInlineApi>();
    await inline!.AnswerInlineQueryAsync(query.QueryId,
        [new TelegramInlineArticle("1", "回显", query.Query)],
        new TelegramInlineAnswerOptions { CacheTime = TimeSpan.Zero, IsPersonal = true });
});

Events.MapPlatform(TelegramEventKinds.PreCheckoutQuery, async evt =>
{
    if (evt.Raw is TelegramPreCheckoutQuery checkout)
        await Context.GetAdapterExtension<ITelegramPaymentApi>()!.AnswerPreCheckoutQueryAsync(checkout.QueryId, ok: true);
});
```

## 原始 Bot API

Model 没有覆盖的方法用 `ITelegramRawApi` 调用，参数使用 Bot API 的 snake_case 名称：

```csharp
using System.Text.Json.Nodes;

var raw = Context.GetAdapterExtension<ITelegramRawApi>()!;
var result = await raw.CallAsync("setChatMenuButton", new JsonObject
{
    ["menu_button"] = new JsonObject { ["type"] = "commands" }
});

await using var file = File.OpenRead("cover.jpg");
await raw.CallAsync("sendPhoto", new JsonObject { ["chat_id"] = 123456, ["photo"] = "attach://cover" },
    [new TelegramUploadFile("cover", "cover.jpg", file)]);
```

适配器负责长轮询：调用 `getUpdates` 会抢走更新，`setWebhook` 会让轮询停止，直到调用 `deleteWebhook`。
