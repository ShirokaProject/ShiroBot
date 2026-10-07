# 入群自动审批策略（IQGroupApprovalStrategyApi）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

```csharp
var api = Context.GetAdapterExtension<IQGroupApprovalStrategyApi>();
if (api is null) return;
```

接口可获取不表示每个方法都已实现；未实现的操作会抛出 `NotSupportedException`。平台权限与参数错误仍可能导致调用失败。

## GetApprovalStrategiesAsync

分页查询入群自动审批策略。

```csharp
Task<QApprovalStrategyPage> GetApprovalStrategiesAsync(
    string? cursor = null,
    int limit = 20,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cursor` | `string?` | `null` | 上一页返回的 NextCursor；null 从第一页开始。 |
| `limit` | `int` | `20` | 本页请求数量。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QApprovalStrategyPage>`。返回策略列表 Strategies 和下一页游标 NextCursor。

## CreateApprovalStrategyAsync

创建入群自动审批策略。

```csharp
Task<QApprovalStrategy> CreateApprovalStrategyAsync(
    QApprovalStrategyOptions options,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `options` | `QApprovalStrategyOptions` | `必传` | 创建策略的配置。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QApprovalStrategy>`。等待异步完成后取得签名所列模型或列表。

## UpdateApprovalStrategyAsync

修改指定入群自动审批策略。

```csharp
Task<QApprovalStrategy> UpdateApprovalStrategyAsync(
    string strategyId,
    QApprovalStrategyUpdate update,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `strategyId` | `string` | `必传` | 平台返回的自动审批策略 ID。 |
| `update` | `QApprovalStrategyUpdate` | `必传` | 需要修改的策略字段。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QApprovalStrategy>`。等待异步完成后取得签名所列模型或列表。

## DeleteApprovalStrategyAsync

删除指定入群自动审批策略。

```csharp
Task DeleteApprovalStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `strategyId` | `string` | `必传` | 平台返回的自动审批策略 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## ExecuteApprovalStrategyAsync

触发平台异步扫描；返回不代表已完成。

```csharp
Task ExecuteApprovalStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `strategyId` | `string` | `必传` | 平台返回的自动审批策略 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## UpdateApprovalWhitelistAsync

加入或移除策略白名单中的 QQ 号码。

```csharp
Task<int> UpdateApprovalWhitelistAsync(
    string strategyId,
    IReadOnlyList<string> qqNumbers,
    bool add = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `strategyId` | `string` | `必传` | 平台返回的自动审批策略 ID。 |
| `qqNumbers` | `IReadOnlyList<string>` | `必传` | QQ 号码字符串列表。 |
| `add` | `bool` | `true` | 是否加入白名单；false 为移除。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<int>`。返回平台报告的白名单变更数量。

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
