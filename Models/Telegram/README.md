# ShiroBot.Model.Telegram

Protocol-neutral Telegram contracts for ShiroBot plugins and adapters.

Telegram contracts are distributed by the `ShiroBot.SDK` package and built into the official host.
A component that uses these contracts should declare the runtime package requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.telegram", MinimumVersion = "1.0.4")]
```

Telegram Model ABI **1.1.0.0** (host 1.0.4) adds, compatibly with 1.0.0.0:

- Entities: `TelegramChat`, `TelegramChatMember`, `TelegramChatPermissions`, `TelegramAdminRights`,
  `TelegramForumTopic`, `TelegramInviteLink`, `TelegramBotCommand` / `TelegramCommandScope`;
  `TelegramUser` gains `LanguageCode` and `IsPremium`.
- Outgoing content for `ITelegramMessageApi.SendAsync`: text with MarkdownV2/HTML, polls and quizzes,
  locations, venues, contacts, dice, stickers, media groups and invoices, with inline keyboards,
  reply keyboards, keyboard removal and force reply.
- Optional adapter extensions: `ITelegramClientApi` (account kind, transport, file limits, self profile),
  `ITelegramChatApi`, `ITelegramMessageApi`, and the bot-only `ITelegramInlineApi`, `ITelegramPaymentApi`,
  `ITelegramBotProfileApi` and `ITelegramBotApi`. The Bot API raw call uses only System.Text.Json types and
  reaches every Bot API method.
- Bot and user-account (MTProto) adapters share this Model under the platform ID `telegram`. Chat IDs always use
  the Bot API format (`TelegramChatIds` converts to and from MTProto peers); raw JSON states its `JsonFormat`.
- `MessageEvent.Raw` carries `TelegramMessage`; content without a common segment arrives as
  `RawSegment` with a `TelegramSegmentKinds` kind. Telegram-only updates arrive as `PlatformEvent`
  with a `TelegramEventKinds` kind and a `TelegramEventPayload` in `Raw`.

Chat parameters accept numeric IDs, `@username` or SDK channel IDs (`chatId:threadId`); user and message
IDs are the SDK `User.Id` and `MessageId` strings. Unimplemented members throw `NotSupportedException`.
See the [Telegram Model guide](../../docs/plugin/telegram-model.md).
