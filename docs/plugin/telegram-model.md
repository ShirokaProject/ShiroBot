# Telegram Model 特有能力

`ShiroBot.Model.Telegram` 补充通用 SDK 无法表达的 Telegram 数据与操作。插件先使用 `ShiroBot.SDK.Models`
处理普通消息，再按需探测 Telegram 扩展接口。Model 由宿主提供，插件不需要引用适配器程序集。

机器人适配器和用户账号适配器共用这一套 Model，平台 ID 都是 `telegram`；各自只实现自己账号能用的接口，
插件通过 `GetAdapterExtension<T>()` 是否返回 `null` 判断。

完整签名见 [接口参考](./telegram-reference) 与 [C# 类型参考](./telegram/types)。

## 常用 Model 类型

各平台类型与 SDK 主消息段的对应关系见[消息段对照](./segments)；类型参考中每个对应类型都标有“对应主消息段”。

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

## ID 与账号类型

- 会话 ID 一律使用 Bot API 格式：用户为正数，普通群为负数，超级群组和频道为 `-100…`；话题频道为 `chatId:threadId`。
  用户账号（MTProto）适配器在内部与 peer 互相转换，插件保存的 ID 在两种适配器之间通用。需要换算时使用 `TelegramChatIds`。
- 会话参数接受数字 ID、`@username` 或 SDK 频道 ID；用户和消息 ID 使用 `User.Id`、`MessageId` 的字符串。Model 实体中的原生 ID 为 `long`。
- `ITelegramClientApi` 说明当前实例的账号类型（`AccountKind`：机器人或用户）、传输方式（`Transport`：Bot API 或 MTProto）
  以及上传、下载上限（官方 Bot API 为 50 MB / 20 MB，自建 Bot API Server 与 MTProto 为 2000 MB）。
- `TelegramMessage.Json` 和事件负载的 `Json` 保留原始对象，结构由 `JsonFormat` 说明：机器人适配器为 Bot API 格式。

## 目前提供的功能

| 功能 | Model 接口 | 内容 |
| --- | --- | --- |
| 账号操作（仅用户账号） | `ITelegramAccountApi` | 会话列表、通过邀请链接加入、标记已读、通讯录、点击其他机器人的回调按钮 |
| 账号信息 | `ITelegramClientApi` | 账号类型、传输方式、文件上限、自身资料、已见用户；两种账号都实现 |
| 会话管理 | `ITelegramChatApi` | 会话资料、成员数、成员与管理员查询；封禁、解封、限制、提升、头衔、默认权限；简介、头像；置顶；邀请链接；入群审批；话题的创建、修改、关闭、重开和删除 |
| 原生消息 | `ITelegramMessageApi` | 发送原生内容；编辑文本、说明和键盘（包括 inline 消息）；转发、复制；输入状态（含上传图片等动作）；结束投票；带提示回答按钮回调（仅机器人） |
| inline 模式（仅机器人） | `ITelegramInlineApi` | 回答 inline 查询 |
| 支付（仅机器人） | `ITelegramPaymentApi` | 付款前确认、收货方式、Telegram Stars 退款与余额；发送发票使用 `TelegramInvoiceContent` |
| 机器人资料（仅机器人） | `ITelegramBotProfileApi` | 命令菜单（按范围和语言）、名称与介绍 |
| Bot API 原始调用（仅 Bot API） | `ITelegramBotApi` | 调用任意 Bot API 方法并上传文件，接收原始 Update；只使用 System.Text.Json 类型 |

改名、退出、踢出、禁言、删除单条消息、成员资料、历史消息等通用操作优先使用 SDK 通用服务，换平台时插件不用改；Model 接口只放通用服务表达不了的部分。

取得接口后，未实现的方法仍可能抛出 `NotSupportedException`。平台权限由 Telegram 校验，失败时抛出 `InvalidOperationException`，消息包含错误码与说明。

## 通用能力支持

| 能力 | Telegram |
| --- | --- |
| 发送 Markdown | 原生 MarkdownV2，语法遵循 Telegram |
| 链接按钮 | 原生 inline keyboard |
| 回调按钮及点击 | 原生，自动 answerCallbackQuery；按钮 ID 与 Data 可往返恢复 |
| 通用卡片 | 无原生卡片；显式允许 `CardAsMarkdown` / `CardAsText` 时由 SDK 降级 |
| 通用 Reaction | Unicode 与自定义表情；设置一个或清除自己的回应 |
| 互动回复 | 支持 |
| @全体成员 | 不支持：Telegram 没有全员提及 |
| 文件上传 | `sendDocument` 同时发布，`IsPublished` 为 true；返回的文件 ID 限定在当前实例和会话 |
| 编辑消息 | 文字、说明、按钮和媒体；不能把文字消息改成媒体，也不能改成贴纸；内容未变化时视为成功 |
| 批量删除 | `deleteMessages`，每批 100 条；48 小时前的消息通常无法删除，群内需要删除权限 |
| 置顶与输入状态 | `pinChatMessage` / `unpinChatMessage`；`sendChatAction` typing，话题内同样有效 |
| 封禁与解封 | `banChatMember` / `unbanChatMember`（解封不会把仍在群内的成员移出） |
| 消息编辑事件 | `edited_message` / `edited_channel_post` 映射为 `MessageEditedEvent` |

逐项对照见[消息段对照](./segments)。

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
| `ChatMemberUpdated` / `MyChatMemberUpdated` | `TelegramChatMemberUpdated` | 成员或机器人自身的状态变化 |
| `ChatJoinRequest` | `TelegramChatJoinRequest` | 入群申请，用 `ITelegramChatApi` 审批 |
| `InlineQuery` / `ChosenInlineResult` | `TelegramInlineQuery` / `TelegramChosenInlineResult` | inline 模式 |
| `Poll` / `PollAnswer` | `TelegramPollUpdated` / `TelegramPollAnswer` | 投票状态与用户选择 |
| `PreCheckoutQuery` / `ShippingQuery` | `TelegramPreCheckoutQuery` / `TelegramShippingQuery` | 支付流程，需及时回答 |
| `ChatBoost` / `ChatBoostRemoved` | `TelegramChatBoost` | 助力变化 |

其他没有建模的 Update 以 Bot API 字段名为 `Kind`，`Raw` 为 `JsonElement`。消息、消息编辑（`MessageEditedEvent`）、按钮回调、成员加入与离开、Reaction
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

## Bot API 原始调用

Model 没有覆盖的方法，在 Bot API 机器人适配器上用 `ITelegramBotApi` 调用，参数使用 Bot API 的 snake_case 名称：

```csharp
using System.Text.Json.Nodes;

var raw = Context.GetAdapterExtension<ITelegramBotApi>()!;
var result = await raw.CallAsync("setChatMenuButton", new JsonObject
{
    ["menu_button"] = new JsonObject { ["type"] = "commands" }
});

await using var file = File.OpenRead("cover.jpg");
await raw.CallAsync("sendPhoto", new JsonObject { ["chat_id"] = 123456, ["photo"] = "attach://cover" },
    [new TelegramUploadFile("cover", "cover.jpg", file)]);
```

适配器负责长轮询：调用 `getUpdates` 会抢走更新，`setWebhook` 会让轮询停止，直到调用 `deleteWebhook`。
