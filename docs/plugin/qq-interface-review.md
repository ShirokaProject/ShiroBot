# QQ 接口审阅（ABI 1.0）

本次直接破坏旧 QQ Model ABI：`ShiroBot.Model.QQ` 为 **1.0.0.0**，不提供旧接口过渡。
宿主 SDK ABI 同时升级为 **1.0.0.0**；所有引用旧 SDK 或 QQ Model 的组件必须重新编译。
宿主会拒绝加载引用旧 QQ Model ABI 的组件，避免运行后才报签名错误。

## 插件入口

常规群管理只需要 `IQGroupApi`。两种适配器使用同一个接口与请求模型。
ID 是当前适配器实例范围内的不透明字符串：官方传 OpenID，Milky 传数值字符串。
字符串化不会让不同协议的 ID 可以互换，请使用产生事件的适配器实例。

```csharp
var groups = context.GetAdapterExtension<IQGroupApi>();
if (groups is null) return;

if ((groups.Capabilities & QGroupCapabilities.JoinRequests) != 0)
{
    var page = await groups.GetJoinRequestsAsync(groupId);
    foreach (var request in page.Requests)
        await groups.AcceptJoinRequestAsync(request);
}
```

## 主要签名

以下为审阅摘要，完整定义位于 `Models/QQ/IQExtensions.cs`。

```csharp
QGroupCapabilities Capabilities { get; }
Task<IReadOnlyList<QGroup>> GetGroupListAsync(bool noCache = false, CancellationToken cancellationToken = default);
Task<QGroup> GetGroupInfoAsync(string groupId, bool noCache = false, CancellationToken cancellationToken = default);
Task<IReadOnlyList<QGroupMember>> GetGroupMemberListAsync(string groupId, bool noCache = false, CancellationToken cancellationToken = default);
Task<QGroupMember> GetGroupMemberInfoAsync(string groupId, string userId, bool noCache = false, CancellationToken cancellationToken = default);
Task MuteMemberAsync(string groupId, string userId, TimeSpan duration, CancellationToken cancellationToken = default);
Task KickMemberAsync(string groupId, string userId, bool rejectAddRequest = false, CancellationToken cancellationToken = default);
Task<QGroupJoinRequestPage> GetJoinRequestsAsync(string groupId, string? cursor = null, int limit = 20, CancellationToken cancellationToken = default);
Task AcceptJoinRequestAsync(QGroupJoinRequest request, CancellationToken cancellationToken = default);
Task RejectJoinRequestAsync(QGroupJoinRequest request, string? reason = null, bool addToBlacklist = false, CancellationToken cancellationToken = default);
Task<QGroupMuteState> GetMuteStateAsync(string groupId, CancellationToken cancellationToken = default);
Task<QBatchOperationResult> SetMemberMutesAsync(string groupId, IReadOnlyList<QMemberMute> members, CancellationToken cancellationToken = default);
```

`QGroupJoinRequest` 必填 `GroupId`、`UserId`、`RequestId`。
可选数据包括 `Username`、`Comment`、`IsInvited`、`IsFiltered`、`InviterId`、`State`、
`RiskTips`、`UnionId`、`IsBot`、`Verification`、`AutoApprovedStrategyId`，
并继承事件的 `Time`、`SelfId`、`EventId`。
将查询或事件返回的完整对象直接交给审批方法；插件无需解析 Milky 通知序号或官方申请 ID。

## 能力差异

| 操作 | Milky | 官方群聊 |
| --- | --- | --- |
| 群列表 | 支持 | 未声明 |
| 群资料、成员资料及成员列表 | 支持 | 支持 |
| 单人禁言、踢人 | 支持 | 支持 |
| 批量成员禁言 | 逐项设置并返回结果 | 逐项设置并返回结果 |
| 入群申请查询、同意及拒绝 | 支持 | 支持 |
| 拒绝申请并拉黑 | 未声明 | 支持 |
| 禁言状态及定时/周期规则查询 | 未声明 | 支持 |
| 自动审批策略与白名单 | 未提供扩展 | 支持 |

`Capabilities` 描述适配器实现情况，不能替代 QQ 平台授权。
自动审批策略使用可选的 `IQGroupApprovalStrategyApi`；策略 `GroupIds` 为适配器 ID，
`GroupNumbers` 为 QQ 群号字符串，两者按平台规则择一。
官方 Markdown、媒体及流式消息仍保留各自扩展接口，因为它们表达额外平台能力。

## 迁移表

| 旧接口或字段 | 新接口或字段 |
| --- | --- |
| 数值账号、群、消息、请求 ID | `string`，可空 ID 为 `string?` |
| `IQOfficialGroupApi` | `IQGroupApi`，策略使用 `IQGroupApprovalStrategyApi` |
| `IQSystemApi` 的群及成员查询 | `IQGroupApi` 同名方法 |
| `QOfficialJoinRequest`、`QGroupInvitedJoinRequest` | `QGroupJoinRequest` |
| 官方成员事件专属 Raw | `QGroupMemberIncrease` / `QGroupMemberDecrease`；额外 OpenID 在 `GlobalUserId` |
| `OfficialGroupJoinRequest` / `GroupInvitedJoinRequest` 事件 Kind | `QEventKinds.GroupJoinRequest` |
| `MessageSeq` | `MessageId` |
| `NotificationSeq` / `InvitationSeq` | `NotificationId` / `InvitationId` |
| 数值分页起点及返回值 | `cursor` / `NextCursor`，字符串 |
| 按序号直接审批 | 传入 `QGroupJoinRequest` |

数值计数、枚举、大小和时间仍保持原有语义；文件大小仍为 `long`。
`QSentMessage.MessageId`、原生消息发送的返回 ID 也改为字符串。

## 本轮补齐的语义

- SDK 身份使用 `UserReference`，权限按实例隔离；消息引用包含会话。
- 所有 QQ 异步服务方法都有 `CancellationToken`；SDK 消息、群/频道、用户服务及带引用的路由也支持取消。
- `QMemberMute` 使用属性初始化，只表达 UserId 和 Duration，不再暴露 UpdateExisting。
- 批量禁言先校验所有输入，再按项执行；没有事务或回滚保证。失败后继续后续项，结果按输入顺序返回。
- `Succeeded` 为确认成功；`Failed` 为明确业务拒绝；`Unknown` 为无法确认服务端结果；`NotExecuted` 为未尝试。
  取消抛 `QBatchOperationCanceledException`，其 `PartialResult` 保留状态。不要无条件重试 Unknown 项。
- 资料缺失为 null；角色未提供或无法识别为 Unknown。显示回退通过 DisplayName 完成。
- `QPatch<T>` 的 default 为未指定，Set(value) 为设置，Clear() 为清空。
  策略备注 Clear 发送空字符串；过期时间未有已确认的清除方式，Clear 明确报 NotSupportedException。
- 通用发送返回失败结果；普通扩展操作抛异常；批量操作返回逐项结果；取消不被吞掉。
