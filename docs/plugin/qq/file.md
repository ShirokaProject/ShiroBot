# 文件（IQFileApi）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

```csharp
var api = Context.GetAdapterExtension<IQFileApi>();
if (api is null) return;
```

接口可获取不表示每个方法都已实现；未实现的操作会抛出 `NotSupportedException`。平台权限与参数错误仍可能导致调用失败。

## UploadPrivateFileAsync

上传私聊文件。

```csharp
Task<string> UploadPrivateFileAsync(
    string userId,
    string fileUri,
    string fileName,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `fileUri` | `string` | `必传` | 文件 URI，由适配器解析。 |
| `fileName` | `string` | `必传` | 文件名。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回上传文件的 ID。

## UploadGroupFileAsync

上传群文件。

```csharp
Task<string> UploadGroupFileAsync(
    string groupId,
    string fileUri,
    string fileName,
    string parentFolderId = "/",
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `fileUri` | `string` | `必传` | 文件 URI，由适配器解析。 |
| `fileName` | `string` | `必传` | 文件名。 |
| `parentFolderId` | `string` | `"/"` | 父文件夹 ID；默认 / 为根目录。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回上传文件的 ID。

## GetPrivateFileDownloadUrlAsync

获取私聊文件下载链接。

```csharp
Task<string> GetPrivateFileDownloadUrlAsync(
    string userId,
    string fileId,
    string fileHash,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `fileId` | `string` | `必传` | 文件 ID。 |
| `fileHash` | `string` | `必传` | 文件哈希。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回下载链接。

## GetPrivateFileDownloadUrlAsync

获取私聊文件下载链接，并指定文件是否由机器人自己发送。

```csharp
Task<string> GetPrivateFileDownloadUrlAsync(
    string userId,
    string fileId,
    string fileHash,
    bool isSelfSend,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `fileId` | `string` | `必传` | 文件 ID。 |
| `fileHash` | `string` | `必传` | 文件哈希。 |
| `isSelfSend` | `bool` | `必传` | 文件是否由机器人自己发送。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回下载链接。

## GetGroupFileDownloadUrlAsync

获取群文件下载链接。

```csharp
Task<string> GetGroupFileDownloadUrlAsync(
    string groupId,
    string fileId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `fileId` | `string` | `必传` | 文件 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回下载链接。

## GetGroupFilesAsync

查询父目录下的群文件和文件夹。

```csharp
Task<(IReadOnlyList<QGroupFile> Files, IReadOnlyList<QGroupFolder> Folders)> GetGroupFilesAsync(
    string groupId,
    string parentFolderId = "/",
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `parentFolderId` | `string` | `"/"` | 父文件夹 ID；默认 / 为根目录。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<(IReadOnlyList<QGroupFile> Files, IReadOnlyList<QGroupFolder> Folders)>`。返回文件列表 Files 和文件夹列表 Folders。

## MoveGroupFileAsync

移动群文件到另一个文件夹。

```csharp
Task MoveGroupFileAsync(
    string groupId,
    string fileId,
    string targetFolderId,
    string parentFolderId = "/",
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `fileId` | `string` | `必传` | 文件 ID。 |
| `targetFolderId` | `string` | `必传` | 目标文件夹 ID。 |
| `parentFolderId` | `string` | `"/"` | 父文件夹 ID；默认 / 为根目录。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## RenameGroupFileAsync

重命名群文件。

```csharp
Task RenameGroupFileAsync(
    string groupId,
    string fileId,
    string newFileName,
    string parentFolderId = "/",
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `fileId` | `string` | `必传` | 文件 ID。 |
| `newFileName` | `string` | `必传` | 新的文件名。 |
| `parentFolderId` | `string` | `"/"` | 父文件夹 ID；默认 / 为根目录。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## DeleteGroupFileAsync

删除群文件。

```csharp
Task DeleteGroupFileAsync(
    string groupId,
    string fileId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `fileId` | `string` | `必传` | 文件 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## CreateGroupFolderAsync

创建群文件夹。

```csharp
Task<string> CreateGroupFolderAsync(
    string groupId,
    string folderName,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `folderName` | `string` | `必传` | 待创建的文件夹名。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回创建的文件夹 ID。

## RenameGroupFolderAsync

重命名群文件夹。

```csharp
Task RenameGroupFolderAsync(
    string groupId,
    string folderId,
    string newFolderName,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `folderId` | `string` | `必传` | 文件夹 ID。 |
| `newFolderName` | `string` | `必传` | 新的文件夹名。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## DeleteGroupFolderAsync

删除群文件夹。

```csharp
Task DeleteGroupFolderAsync(
    string groupId,
    string folderId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `folderId` | `string` | `必传` | 文件夹 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## PersistGroupFileAsync

把群文件转存为永久文件(阻止过期)。

```csharp
Task PersistGroupFileAsync(
    string groupId,
    string fileId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `fileId` | `string` | `必传` | 文件 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
