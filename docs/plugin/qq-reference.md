# QQ C# 接口参考

这里按插件调用方式记录全部 QQ Model 扩展接口，包含 C# 方法签名、参数、默认值与返回类型。
无需构造 HTTP 请求。插件通过 `Context.GetAdapterExtension<T>()` 获取当前实例的服务。

SDK 与 QQ Model ABI 均为 **1.0.0.0**；所有引用旧 ABI 的组件需重新编译。
所有用户、群、消息、申请 ID 均使用字符串。数值字符串与 OpenID 仍属于各自的来源实例。

## 服务接口

| 分类 | C# 接口 | 文档 |
| --- | --- | --- |
| 好友 | `IQFriendApi` | [好友](./qq/friend) |
| 群管理 | `IQGroupApi` | [群管理](./qq/group) |
| 入群自动审批策略 | `IQGroupApprovalStrategyApi` | [入群自动审批策略](./qq/approval) |
| 文件 | `IQFileApi` | [文件](./qq/file) |
| 账号与资料 | `IQSystemApi` | [账号与资料](./qq/system) |
| 原生消息 | `IQMessageApi` | [原生消息](./qq/message) |
| 官方消息 | `IQOfficialMessageApi` | [官方消息](./qq/official-message) |
| 官方媒体 | `IQOfficialMediaApi` | [官方媒体](./qq/official-media) |
| 官方私聊 | `IQOfficialDirectMessageApi` | [官方私聊](./qq/official-direct) |
| 官方流式会话 | `IQOfficialMessageStream` | [官方流式会话](./qq/official-stream) |

## 模型与迁移

- [完整 C# 类型：实体、消息、消息段、事件、按钮和策略](./qq/types)
- [ABI 变更及迁移表](./qq-interface-review)
- [官方 Markdown 与按钮使用示例](./qq-official)

## 适配器支持

群管理通过 `IQGroupApi.Capabilities` 检查具体操作。其他扩展先检查服务是否为 null；
拿到服务后，未实现的方法仍可能抛出 `NotSupportedException`。
官方自动审批策略使用独立的 `IQGroupApprovalStrategyApi`；平台权限由 QQ 管理。
