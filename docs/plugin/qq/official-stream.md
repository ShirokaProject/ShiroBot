# 官方流式会话（IQOfficialMessageStream）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

由 `IQOfficialDirectMessageApi.BeginStream` 返回；使用完调用 `DisposeAsync` 或 `await using`。

## HasStarted

当前服务状态或实现能力。

```csharp
bool HasStarted { get; }
```

## AppendAsync

更新流式回复的完整可见文本。

```csharp
Task AppendAsync(string cumulativeText, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cumulativeText` | `string` | `必传` | 截至本次调用的完整可见文本，不是新增片段。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## CompleteAsync

完成流式回复，返回最终消息 ID。

```csharp
Task<string> CompleteAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回完成后的消息 ID。

## DisposeAsync（继承自 IAsyncDisposable）

释放会话资源。建议使用 `await using`，是否取消未完成回复由适配器实现决定。

```csharp
ValueTask DisposeAsync();
```

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
