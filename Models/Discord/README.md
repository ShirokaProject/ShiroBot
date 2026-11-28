# ShiroBot.Model.Discord

Protocol-neutral Discord contracts for ShiroBot plugins and adapters.

Discord contracts are distributed by the `ShiroBot.SDK` package and built into the official host.
A component that uses these contracts should declare the runtime package requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.discord", MinimumVersion = "1.0.4")]
```

Discord Model ABI **1.1.0.0** (host 1.0.4) adds, compatibly with 1.0.0.0:

- Entities: `DiscordGuild`, `DiscordChannel`, `DiscordRole`, `DiscordMember`, `DiscordPermissions`,
  `DiscordEmoji`, `DiscordInvite`, `DiscordBan`; `DiscordUser` gains `AvatarUrl`.
- Rich messages (`DiscordMessageContent`): embeds, buttons, select menus, raw Components V2,
  attachments, stickers, polls, allowed mentions, flags and replies.
- Interactions: `DiscordInteraction` (in `InteractionEvent.Raw`) with command options, select values and
  modal input; `DiscordApplicationCommand` definitions and `DiscordModal`.
- Optional adapter extensions: `IDiscordClientApi` (account kind, profile, presence), `IDiscordGuildApi`,
  `IDiscordMessageApi`, `IDiscordRawApi`, the bot-only `IDiscordInteractionApi` and the user-only
  `IDiscordAccountApi`. Bot and user-account adapters share this Model under the platform ID `discord`.
  The raw API uses only System.Text.Json types and reaches every
  REST route and Gateway command.
- `MessageEvent.Raw` carries `DiscordMessage`; embeds, stickers, polls and components arrive as `RawSegment`
  with a `DiscordSegmentKinds` kind. Discord-only Gateway events arrive as `PlatformEvent` with a
  `DiscordEventKinds` kind and a `DiscordEventPayload` in `Raw`.

ID parameters are Snowflake strings, the same as SDK `User.Id`, `Channel.Id` and `MessageId`.
Unimplemented members throw `NotSupportedException`.
See the [Discord Model guide](../../docs/plugin/discord-model.md).
