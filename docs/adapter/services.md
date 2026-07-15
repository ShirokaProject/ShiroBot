# 实现服务接口

`IBotAdapter` 将能力拆成六组服务。接口中的方法都有默认实现，未实现的方法会抛出 `NotSupportedException`，所以适配器可以先完成协议支持的部分。

## 服务分组

| 服务 | 主要能力 |
| --- | --- |
| `IMessageService` | 发送、撤回、查询消息，历史消息和转发消息 |
| `IGroupService` | 群设置、成员管理、群请求、公告、精华和表情回应 |
| `IFriendService` | 好友请求、点赞、戳一戳和删除好友 |
| `IFileService` | 私聊/群文件上传、下载和目录管理 |
| `ISystemService` | 登录信息、实现信息、好友/群列表和账号资料 |
| `IEventService` | 从协议实现端向宿主上报事件 |

## 消息服务示例

```csharp
using ShiroBot.Model.Message.Requests;
using ShiroBot.Model.Message.Responses;
using ShiroBot.SDK.Adapter;

public sealed class ExampleMessageService : IMessageService
{
    public Task<SendPrivateMessageResponse> SendPrivateMessageAsync(
        SendPrivateMessageRequest request) =>
        ProtocolClient.RequestAsync<
            SendPrivateMessageRequest,
            SendPrivateMessageResponse>("send_private_message", request);

    public Task<SendGroupMessageResponse> SendGroupMessageAsync(
        SendGroupMessageRequest request) =>
        ProtocolClient.RequestAsync<
            SendGroupMessageRequest,
            SendGroupMessageResponse>("send_group_message", request);

    public Task RecallGroupMessageAsync(RecallGroupMessageRequest request) =>
        ProtocolClient.RequestAsync("recall_group_message", request);
}
```

适配器负责把协议响应完整转换为 `ShiroBot.Model` 中对应的 Response 类型。不要把协议库自己的 DTO 暴露到 SDK 接口外。

## 空服务

尚未支持某组能力时可以使用空实现：

```csharp
public sealed class ExampleGroupService : IGroupService { }
public sealed class ExampleFriendService : IFriendService { }
public sealed class ExampleFileService : IFileService { }
```

插件调用这些默认方法时会得到 `NotSupportedException`。这比返回伪造的成功结果更容易定位兼容性问题。

::: warning 适配器版本规则
适配器必须针对宿主使用的 SDK 版本重新编译。`IEventService` 只保留统一的 `EventReceived(Event)` 事件，SDK 不再承诺旧适配器二进制兼容；插件 API 与 Model ABI 兼容策略不受影响。
:::

## 系统服务最低建议

建议至少实现：

```csharp
Task<GetLoginInfoResponse> GetLoginInfoAsync();
Task<GetImplInfoResponse> GetImplInfoAsync();
```

它们用于确认机器人登录身份和底层实现版本。随后根据协议能力实现好友、群组和成员查询。

## 模型映射原则

1. **ID 不要损失精度**：用户、群、消息序号使用 `long` 或模型声明的类型。
2. **时间语义一致**：明确协议返回的是 Unix 秒、毫秒还是本地时间。
3. **消息段保持顺序**：文本、图片、Mention、回复等 Segment 顺序会影响实际消息。
4. **未知类型可观测**：记录未知事件或消息段，不要静默吞掉。
5. **取消与超时**：底层 HTTP/WebSocket 客户端应设置合理超时，并在断线后退避重连。
6. **异常保留上下文**：错误信息应包含协议动作名称和响应错误码，但不能泄露 token。

## 请求与响应模型

统一模型按功能分布在：

```text
ShiroBot.Model.Common
ShiroBot.Model.Message.Requests / Responses
ShiroBot.Model.Group.Requests / Responses
ShiroBot.Model.Friend.Requests / Responses
ShiroBot.Model.File.Requests / Responses
ShiroBot.Model.System.Requests / Responses
```

开发时优先让 IDE 根据接口方法签名导入准确命名空间，避免创建同名 DTO。
