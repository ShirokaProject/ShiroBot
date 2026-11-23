# 消息段对照

插件收发消息只用 SDK 的消息段，换平台不用改代码。本页列出每个 SDK 主消息段在 QQ、Telegram、Discord Model 里
对应的类型，以及适配器如何处理它；点击类型跳到该平台的 C# 类型参考，那里的“对应主消息段”会链接回本页。

Model 类型只在插件主动调用平台扩展接口（如 `IQMessageApi`、`ITelegramMessageApi`）时用到；
平台独有、SDK 表达不了的内容见 [RawSegment](#rawsegment)。通用消息段的字段见 [通用 Model](./models#消息段)。

| 主消息段 | 说明 |
| --- | --- |
| [`TextSegment`](#textsegment) | 纯文本。 |
| [`MarkdownSegment`](#markdownsegment) | Markdown 正文，各平台语法不同。 |
| [`MentionSegment`](#mentionsegment) | 提及用户。 |
| [`MentionAllSegment`](#mentionallsegment) | 提及全体成员。 |
| [`QuoteSegment`](#quotesegment) | 引用（回复）一条消息。 |
| [`EmojiSegment`](#emojisegment) | 平台表情 ID，可带降级文本。 |
| [`ImageSegment`](#imagesegment) | 图片。 |
| [`AudioSegment`](#audiosegment) | 音频与语音。 |
| [`VideoSegment`](#videosegment) | 视频。 |
| [`FileSegment`](#filesegment) | 文件。 |
| [`CardSegment`](#cardsegment) | 标题、说明、图片、链接和字段组成的卡片。 |
| [`MessageButton`](#messagebutton) | 按钮（不是消息段，放在 `OutgoingMessage.Buttons`），点击产生 `InteractionEvent`。 |
| [`RawSegment`](#rawsegment) | 平台独有内容，含 `Platform`、`Kind`、`Payload` |

## TextSegment

纯文本。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingText`](./qq/types#qincomingtext)：入站<br>[`QOutgoingText`](./qq/types#qoutgoingtext)：出站 | 收发 |
| Telegram | [`TelegramTextContent`](./telegram/types#telegramtextcontent)：原生发送<br>[`TelegramFormattedText`](./telegram/types#telegramformattedtext)：入站格式实体，随文本段一起以 `RawSegment` 上报 | 收发 |
| Discord | [`DiscordMessageContent`](./discord/types#discordmessagecontent)：`Content` 字段 | 收发 |

## MarkdownSegment

Markdown 正文，各平台语法不同。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingMarkdown`](./qq/types#qincomingmarkdown)：入站<br>[`QOfficialMarkdown`](./qq/types#qofficialmarkdown)：官方机器人 Markdown，经 `IQOfficialMessageApi` 发送<br>[`QCustomMarkdown`](./qq/types#qcustommarkdown)：官方机器人自定义 Markdown | 取决于 QQ 适配器；官方机器人走 `IQOfficialMessageApi` |
| Telegram | [`TelegramFormattedText`](./telegram/types#telegramformattedtext)：入站格式实体<br>[`TelegramTextContent`](./telegram/types#telegramtextcontent)：`ParseMode` 指定出站格式 | 原生 MarkdownV2，语法遵循 Telegram |
| Discord | [`DiscordMessageContent`](./discord/types#discordmessagecontent)：`Content` 直接使用 Discord Markdown | 原生 Discord Markdown |

## MentionSegment

提及用户。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingMention`](./qq/types#qincomingmention)：入站<br>[`QOutgoingMention`](./qq/types#qoutgoingmention)：出站 | 收发 |
| Telegram | 没有专门类型 | 数字 ID 发成 `tg://user?id` 链接，`@username` 入站解析为用户 ID；是否通知受平台规则限制 |
| Discord | [`DiscordAllowedMentions`](./discord/types#discordallowedmentions)：控制哪些提及会通知 | 原生 `<@id>` |

## MentionAllSegment

提及全体成员。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingMentionAll`](./qq/types#qincomingmentionall)：入站<br>[`QOutgoingMentionAll`](./qq/types#qoutgoingmentionall)：出站 | 收发 |
| Telegram | Telegram 没有全员提及 | 不支持 |
| Discord | [`DiscordAllowedMentions`](./discord/types#discordallowedmentions)：`Everyone` 控制是否通知 | 入站 @everyone / @here；出站 @everyone |

## QuoteSegment

引用（回复）一条消息。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingReply`](./qq/types#qincomingreply)：入站，可带被引用内容<br>[`QOutgoingReply`](./qq/types#qoutgoingreply)：出站 | 收发 |
| Telegram | [`TelegramSendOptions`](./telegram/types#telegramsendoptions)：`ReplyToMessageId` | `reply_parameters` 引用回复 |
| Discord | [`DiscordMessageContent`](./discord/types#discordmessagecontent)：`ReplyToMessageId` | 引用回复 |

## EmojiSegment

平台表情 ID，可带降级文本。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingFace`](./qq/types#qincomingface)：入站 QQ 表情<br>[`QOutgoingFace`](./qq/types#qoutgoingface)：出站 QQ 表情 | 收发 |
| Telegram | [`TelegramStickerContent`](./telegram/types#telegramstickercontent)：贴纸 | Unicode 表情或贴纸 file_id；`tgfile://` 贴纸优先原生发送 |
| Discord | [`DiscordEmoji`](./discord/types#discordemoji)：服务器自定义表情<br>[`DiscordSticker`](./discord/types#discordsticker)：贴纸，另以 `RawSegment` 上报 | Unicode 或自定义表情标记 |

## ImageSegment

图片。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingImage`](./qq/types#qincomingimage)：入站<br>[`QOutgoingImage`](./qq/types#qoutgoingimage)：出站 | 收发 |
| Telegram | [`TelegramMediaItem`](./telegram/types#telegrammediaitem)：`Kind = Photo`，用于相册 | 入站映射，出站真实媒体上传 |
| Discord | [`DiscordAttachment`](./discord/types#discordattachment)：附件 | 入站映射，出站真实附件上传 |

## AudioSegment

音频与语音。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingRecord`](./qq/types#qincomingrecord)：入站语音<br>[`QOutgoingRecord`](./qq/types#qoutgoingrecord)：出站语音 | 收发 |
| Telegram | [`TelegramMediaItem`](./telegram/types#telegrammediaitem)：`Kind = Audio`，用于相册 | 入站映射，出站真实媒体上传 |
| Discord | [`DiscordAttachment`](./discord/types#discordattachment)：附件 | 入站映射，出站真实附件上传 |

## VideoSegment

视频。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingVideo`](./qq/types#qincomingvideo)：入站<br>[`QOutgoingVideo`](./qq/types#qoutgoingvideo)：出站 | 收发 |
| Telegram | [`TelegramMediaItem`](./telegram/types#telegrammediaitem)：`Kind = Video`，用于相册 | 入站映射，出站真实媒体上传 |
| Discord | [`DiscordAttachment`](./discord/types#discordattachment)：附件 | 入站映射，出站真实附件上传 |

## FileSegment

文件。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QIncomingFile`](./qq/types#qincomingfile)：入站；QQ Model 没有出站文件段 | 取决于 QQ 适配器 |
| Telegram | [`TelegramMediaItem`](./telegram/types#telegrammediaitem)：`Kind = Document`，用于相册 | 入站映射，出站真实文件上传 |
| Discord | [`DiscordAttachment`](./discord/types#discordattachment)：附件 | 入站映射，出站真实附件上传 |

## CardSegment

标题、说明、图片、链接和字段组成的卡片。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | 没有专门类型，图文卡片直接使用 `CardSegment` | 取决于 QQ 适配器 |
| Telegram | 没有原生卡片 | 显式允许 `CardAsMarkdown` / `CardAsText` 时由 SDK 降级 |
| Discord | [`DiscordEmbed`](./discord/types#discordembed)：Embed | 原生 Embed |

## MessageButton

按钮（不是消息段，放在 `OutgoingMessage.Buttons`），点击产生 `InteractionEvent`。

| 平台 | Model 类型 | 适配器处理 |
| --- | --- | --- |
| QQ | [`QInlineKeyboard`](./qq/types#qinlinekeyboard)：官方机器人内联键盘<br>[`QOfficialKeyboard`](./qq/types#qofficialkeyboard)：官方机器人键盘 | 取决于 QQ 适配器 |
| Telegram | [`TelegramInlineKeyboard`](./telegram/types#telegraminlinekeyboard)：内联键盘<br>[`TelegramInlineButton`](./telegram/types#telegraminlinebutton)：单个按钮 | 原生内联键盘，点击为 callback_query |
| Discord | [`DiscordActionRow`](./discord/types#discordactionrow)：按钮行<br>[`DiscordButton`](./discord/types#discordbutton)：单个按钮 | 原生组件，点击为交互 |

## RawSegment

通用模型不能表示的平台原生内容。插件先检查 `Platform` 和 `Kind`，再把 `Payload` 转成对应类型。

### QQ

QQ Model 的这些消息段通过 `IQMessageApi` 收发；它们在 SDK 消息里是否以 `RawSegment` 出现取决于 QQ 适配器。

| 内容 | Model 类型 |
| --- | --- |
| 合并转发 | [`QIncomingForward`](./qq/types#qincomingforward)：入站<br>[`QOutgoingForward`](./qq/types#qoutgoingforward)：出站<br>[`QForwardedMessage`](./qq/types#qforwardedmessage)：转发中的一条消息 |
| 商城表情 | [`QIncomingMarketFace`](./qq/types#qincomingmarketface)：入站 |
| 小程序与卡片 | [`QIncomingLightApp`](./qq/types#qincominglightapp)：入站<br>[`QOutgoingLightApp`](./qq/types#qoutgoinglightapp)：出站 |
| XML 卡片 | [`QIncomingXml`](./qq/types#qincomingxml)：入站 |

### Telegram

`Platform` 为 `telegram`，`Kind` 使用 `TelegramSegmentKinds`；出站使用 `ITelegramMessageApi.SendAsync` 和对应的内容类型。

| 内容 | Model 类型 |
| --- | --- |
| `location` | [`TelegramLocation`](./telegram/types#telegramlocation)：入站负载<br>[`TelegramLocationContent`](./telegram/types#telegramlocationcontent)：出站 |
| `venue` | [`TelegramVenue`](./telegram/types#telegramvenue)：入站负载<br>[`TelegramVenueContent`](./telegram/types#telegramvenuecontent)：出站 |
| `contact` | [`TelegramContact`](./telegram/types#telegramcontact)：入站负载<br>[`TelegramContactContent`](./telegram/types#telegramcontactcontent)：出站 |
| `dice` | [`TelegramDice`](./telegram/types#telegramdice)：入站负载<br>[`TelegramDiceContent`](./telegram/types#telegramdicecontent)：出站 |
| `poll` | [`TelegramPoll`](./telegram/types#telegrampoll)：入站负载<br>[`TelegramPollContent`](./telegram/types#telegrampollcontent)：出站 |
| `successful_payment` | [`TelegramSuccessfulPayment`](./telegram/types#telegramsuccessfulpayment)：入站负载<br>[`TelegramInvoiceContent`](./telegram/types#telegraminvoicecontent)：出站发票 |
| `formatted_text` | [`TelegramFormattedText`](./telegram/types#telegramformattedtext)：入站负载，与文本段一同出现 |

### Discord

`Platform` 为 `discord`，`Kind` 使用 `DiscordSegmentKinds`；Embed 和贴纸也能以 `RawSegment` 直接发送。

| 内容 | Model 类型 |
| --- | --- |
| `embed` | [`DiscordEmbed`](./discord/types#discordembed)：负载；发送时同样接受 |
| `sticker` | [`DiscordSticker`](./discord/types#discordsticker)：负载 |
| `poll` | [`DiscordPoll`](./discord/types#discordpoll)：负载 |
| `components` | [`DiscordComponents`](./discord/types#discordcomponents)：负载，原始 JSON |
