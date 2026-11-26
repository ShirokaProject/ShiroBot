# 文件（IQFileApi）

命名空间：`ShiroBot.Model.QQ`。

```csharp
var api = Context.GetAdapterExtension<IQFileApi>();
if (api is null) return;
```

未实现的方法抛出 `NotSupportedException`；调用还受平台权限和参数约束。

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

`Task`。完成后无返回值。

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

`Task`。完成后无返回值。

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

`Task`。完成后无返回值。

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

`Task`。完成后无返回值。

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

`Task`。完成后无返回值。

## PersistGroupFileAsync

将群文件转存为永久文件。

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

`Task`。完成后无返回值。

## ForwardGroupFileToGroupAsync

把群文件转发到另一个群，由服务端转存，不重新上传。QQ Model `1.2.0.0` 新增；目前的 QQ 适配器都还没有实现，调用会抛出 `NotSupportedException`。

```csharp
Task<string> ForwardGroupFileToGroupAsync(
    string groupId,
    string fileId,
    string targetGroupId,
    string targetFolderId = "/",
    bool sendMessage = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 源文件所在的群 ID。 |
| `fileId` | `string` | `必传` | 源文件 ID。 |
| `targetGroupId` | `string` | `必传` | 目标群 ID。 |
| `targetFolderId` | `string` | `"/"` | 目标群里的文件夹 ID，默认根目录。 |
| `sendMessage` | `bool` | `true` | 是否在目标群里发出这条文件消息。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回目标群里的新文件 ID。

## ForwardGroupFileToFriendAsync

把群文件作为私聊文件转发给好友。QQ Model `1.2.0.0` 新增；目前的 QQ 适配器都还没有实现，调用会抛出 `NotSupportedException`。

```csharp
Task<string> ForwardGroupFileToFriendAsync(
    string groupId,
    string fileId,
    string userId,
    bool sendMessage = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 源文件所在的群 ID。 |
| `fileId` | `string` | `必传` | 源文件 ID。 |
| `userId` | `string` | `必传` | 目标好友 QQ 号。 |
| `sendMessage` | `bool` | `true` | 是否在私聊里发出这条文件消息。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回新的文件 ID。

## UploadFlashFilesAsync

上传文件并创建闪传分享。上传进度通过 `QEventKinds.FlashFileTransfer` 事件上报。QQ Model `1.2.0.0` 新增；目前的 QQ 适配器都还没有实现，调用会抛出 `NotSupportedException`。

```csharp
Task<QFlashShare> UploadFlashFilesAsync(
    IReadOnlyList<string> fileUris,
    string? title = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `fileUris` | `IReadOnlyList<string>` | `必传` | 要上传的文件，支持 http(s)://、file://、base64://。 |
| `title` | `string?` | `null` | 闪传标题；null 由协议端决定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QFlashShare>`。返回文件集 ID、分享链接和过期时间。

## DownloadFlashFilesAsync

开始下载闪传文件集。方法在下载开始后返回；进度和完成通过 `QEventKinds.FlashFileTransfer` 事件上报，完成事件的文件列表带本地路径。QQ Model `1.2.0.0` 新增；目前的 QQ 适配器都还没有实现，调用会抛出 `NotSupportedException`。

```csharp
Task DownloadFlashFilesAsync(
    string shareLinkOrFileSetId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `shareLinkOrFileSetId` | `string` | `必传` | 分享链接（http(s):// 开头）或文件集 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。下载开始后完成，不等下载结束。

## GetFlashFileSetAsync

获取闪传文件集详情。QQ Model `1.2.0.0` 新增；目前的 QQ 适配器都还没有实现，调用会抛出 `NotSupportedException`。

```csharp
Task<QFlashFileSet> GetFlashFileSetAsync(
    string shareLinkOrFileSetId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `shareLinkOrFileSetId` | `string` | `必传` | 分享链接（http(s):// 开头）或文件集 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QFlashFileSet>`。返回标题、分享链接、总大小和文件列表。

## ReshareFlashFilesAsync

重新分享还没过期的闪传文件集。QQ Model `1.2.0.0` 新增；目前的 QQ 适配器都还没有实现，调用会抛出 `NotSupportedException`。

```csharp
Task<QFlashShare> ReshareFlashFilesAsync(
    string fileSetId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `fileSetId` | `string` | `必传` | 文件集 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QFlashShare>`。返回新的分享链接和过期时间。

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
