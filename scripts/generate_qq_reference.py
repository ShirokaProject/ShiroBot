from pathlib import Path
import re
root=Path(__file__).resolve().parent.parent; out=root/'docs/plugin/qq'
out.mkdir(exist_ok=True)
services=[('IQFriendApi','friend','好友'),('IQGroupApi','group','群管理'),('IQGroupApprovalStrategyApi','approval','入群自动审批策略'),('IQFileApi','file','文件'),('IQSystemApi','system','账号与资料'),('IQMessageApi','message','原生消息'),('IQOfficialMessageApi','official-message','官方消息'),('IQOfficialMediaApi','official-media','官方媒体'),('IQOfficialDirectMessageApi','official-direct','官方私聊'),('IQOfficialMessageStream','official-stream','官方流式会话')]
sources='\n'.join((root/'Models/QQ'/f).read_text() for f in ['IQExtensions.cs','IQGroupApprovalStrategyApi.cs'])
def closing(s,i):
 depth=0
 for j in range(i,len(s)):
  if s[j]=='{':depth+=1
  elif s[j]=='}':
   depth-=1
   if depth==0:return j
 raise ValueError(i)
def split_params(s):
 res=[]; start=0;depth=0
 for i,c in enumerate(s):
  if c in '<([':depth+=1
  elif c in '>)]':depth-=1
  elif c==',' and depth==0:res.append(s[start:i].strip());start=i+1
 if s[start:].strip():res.append(s[start:].strip())
 return res
meaning={'message':'类型化的官方出站消息。','cumulativeText':'截至本次调用的完整可见文本，不是新增片段。','userId':'当前实例内的用户 ID。','groupId':'当前实例内的群 ID。','peerId':'会话 ID，与 scene 对应。','messageId':'当前会话的消息 ID。','cursor':'上一页返回的 NextCursor；null 从第一页开始。','limit':'本页请求数量。','cancellationToken':'取消令牌；默认不主动取消。','noCache':'是否绕过协议端缓存。','duration':'禁言或输入状态的持续时间；零禁言时长表示解除禁言。','request':'查询或事件返回的完整入群申请对象。','addToBlacklist':'拒绝申请时是否同时拉黑；需适配器支持。','reason':'可选的拒绝理由。','target':'官方消息目标，含场景与 ID。','reply':'被动回复的消息或事件凭据；null 表示未指定。','content':'待发送的文本、Markdown 或可读媒体流，具体类型见签名。','fileName':'文件名。','fileUri':'文件 URI，由适配器解析。','imageUri':'图片 URI，由适配器解析。','members':'本次操作的成员及禁言时长列表。','segments':'QQ 原生出站消息段。','scene':'好友、群或临时会话场景。','strategyId':'平台返回的自动审批策略 ID。','qqNumbers':'QQ 号码字符串列表。','options':'创建策略的配置。','update':'需要修改的策略字段。','isFiltered':'是否处理被协议端过滤的请求。','initiatorUid':'好友请求返回的发起者 UID。','invitationId':'群邀请事件返回的邀请 ID。','reactionType':'QQ 表情或 Emoji 回应类型。','isAdd':'是否添加回应；false 为移除。','parentFolderId':'父文件夹 ID；默认 / 为根目录。','keyboard':'可选的消息底部按钮。','media':'已上传的媒体引用。','type':'媒体类型。','caption':'随媒体一起发送的文字说明。','forwardId':'合并转发资源 ID。','resourceId':'收到的资源 ID。','interactionId':'按钮互动事件的 InteractionId。','code':'回应按钮互动的结果代码。','contentType':'流式回复内容类型。','isSelf':'是否戳机器人自己。','isSet':'是否设置；false 为取消。','isMute':'是否开启全员禁言。','rejectAddRequest':'踢人后是否拒绝再次入群，依平台语义执行。','isSelfSend':'文件是否由机器人自己发送。','isPinned':'是否置顶会话。','add':'是否加入白名单；false 为移除。','pageIndex':'页索引，按协议端约定。','pageSize':'每页数量。','count':'请求执行的次数。','announcementId':'群公告 ID。','fileId':'文件 ID。','fileHash':'文件哈希。','folderId':'文件夹 ID。','targetFolderId':'目标文件夹 ID。','domain':'需要查询 Cookie 的域名。','templateId':'平台批准的 Ark 模板编号。','fields':'模板字段及内容。','embed':'Embed 卡片内容。','markdown':'自定义 Markdown 或模板 Markdown。','name':'群名。','nickname':'账号昵称。','bio':'账号简介。','card':'群名片。','title':'成员专属头衔。','newFileName':'新的文件名。','newFolderName':'新的文件夹名。','folderName':'待创建的文件夹名。','faceId':'QQ 表情 ID。','reactionId':'回应 ID，与 reactionType 对应。'}

actions={
'SendNudgeAsync':'发送戳一戳。','SendProfileLikeAsync':'给用户资料名片点赞。','DeleteFriendAsync':'删除好友。','AcceptFriendRequestAsync':'接受好友申请。','RejectFriendRequestAsync':'拒绝好友申请。',
'GetGroupListAsync':'查询当前账号的群列表。','GetGroupInfoAsync':'查询指定群的资料。','GetGroupMemberListAsync':'查询指定群的成员列表。','GetGroupMemberInfoAsync':'查询群内指定成员的资料。','SetGroupNameAsync':'修改群名。','SetGroupAvatarAsync':'修改群头像。','SetMemberCardAsync':'修改群成员名片。','SetMemberSpecialTitleAsync':'修改群成员专属头衔。','SetMemberAdminAsync':'设置或取消群管理员。','MuteMemberAsync':'禁言指定成员，零时长解除禁言。','SetWholeMuteAsync':'设置或取消全员禁言。','KickMemberAsync':'移出指定群成员。','QuitGroupAsync':'让机器人退出指定群。','SendMessageReactionAsync':'添加或移除消息表情回应。','GetAnnouncementsAsync':'查询群公告。','SendAnnouncementAsync':'发布群公告。','DeleteAnnouncementAsync':'删除指定群公告。','GetEssenceMessagesAsync':'查询一页群精华消息。','SetEssenceMessageAsync':'设置或取消精华消息。','RejectJoinRequestAsync':'拒绝入群申请，可选择拒绝理由及同时拉黑。','GetMuteStateAsync':'查询群禁言状态和平台返回的定时、周期规则。','SetMemberMutesAsync':'按项设置成员禁言，零时长解除禁言。先验证全部参数，再逐项执行；失败不回滚已完成的项，继续处理剩余项。','AcceptInvitationAsync':'接受邀请机器人入群。','RejectInvitationAsync':'拒绝邀请机器人入群。',
'UploadPrivateFileAsync':'上传私聊文件。','UploadGroupFileAsync':'上传群文件。','GetPrivateFileDownloadUrlAsync':'获取私聊文件下载链接。','GetGroupFileDownloadUrlAsync':'获取群文件下载链接。','GetGroupFilesAsync':'查询父目录下的群文件和文件夹。','MoveGroupFileAsync':'移动群文件到另一个文件夹。','RenameGroupFileAsync':'重命名群文件。','DeleteGroupFileAsync':'删除群文件。','CreateGroupFolderAsync':'创建群文件夹。','RenameGroupFolderAsync':'重命名群文件夹。','DeleteGroupFolderAsync':'删除群文件夹。',
'GetUserProfileAsync':'查询用户资料。','GetFriendListAsync':'查询好友列表。','GetFriendInfoAsync':'查询好友资料。','SetAvatarAsync':'修改机器人账号头像。','SetNicknameAsync':'修改机器人账号昵称。','SetBioAsync':'修改机器人账号简介。','GetCookiesAsync':'查询指定域名的账号 Cookie。','GetCsrfTokenAsync':'查询账号 CSRF Token。',
'RecallMessageAsync':'撤回指定消息。','GetForwardedMessagesAsync':'读取合并转发中的原生消息。','MarkAsReadAsync':'将指定会话标记为已读。',
'GetApprovalStrategiesAsync':'分页查询入群自动审批策略。','CreateApprovalStrategyAsync':'创建入群自动审批策略。','UpdateApprovalStrategyAsync':'修改指定入群自动审批策略。','DeleteApprovalStrategyAsync':'删除指定入群自动审批策略。','UpdateApprovalWhitelistAsync':'加入或移除策略白名单中的 QQ 号码。',
'SendTypingAsync':'发送私聊输入状态，使用被动回复凭据。','BeginStream':'创建流式回复会话，结束后需释放。','AppendAsync':'更新流式回复的完整可见文本。','CompleteAsync':'完成流式回复，返回最终消息 ID。'
}
return_meanings={
'SetMemberMutesAsync':'返回每项的 Succeeded、Failed、Unknown 或 NotExecuted 状态。Unknown 不能假定服务端未执行；取消抛 QBatchOperationCanceledException，可读取 PartialResult。','GetGroupFilesAsync':'返回文件列表 Files 和文件夹列表 Folders。','GetEssenceMessagesPageAsync':'返回消息列表 Messages 与是否最后一页 IsEnd。','GetNotificationsAsync':'返回通知列表 Notifications 和下一页游标 NextCursor。','GetHistoryMessagesAsync':'返回消息列表 Messages 和下一页游标 NextCursor。','GetPeerPinsAsync':'返回置顶好友 Friends 与置顶群 Groups。','GetJoinRequestsAsync':'返回申请列表 Requests 和下一页游标 NextCursor；null 表示没有下一页。','GetApprovalStrategiesAsync':'返回策略列表 Strategies 和下一页游标 NextCursor。','UpdateApprovalWhitelistAsync':'返回平台报告的白名单变更数量。','GetMessageAsync':'返回找到的原生消息；不存在时为 null。','CanSendMarkdown':'返回适配器是否支持此目标与按钮形式；true 不保证平台授权。','BeginStream':'返回可异步释放的流式回复会话。','UploadAsync':'返回可复用的上传媒体引用。','SendMessageDetailedAsync':'返回消息 ID 与发送时间。'
}

count=0
for interface,slug,label in services:
 m=re.search(r'public interface '+interface+r'\b[^\{]*\{',sources); start=m.end();body=sources[start:closing(sources,m.end()-1)]
 # Capture only top-level declarations; skip default method bodies and arrow expressions.
 entries=[]; pos=0;comments=[]
 while pos<len(body):
  while pos<len(body) and body[pos].isspace():pos+=1
  if pos>=len(body):break
  if body.startswith('///',pos):
   end=body.find('\n',pos);comments.append(body[pos+3:end].strip());pos=end+1;continue
  end=pos
  while end<len(body) and body[end] not in '{;':
   if body.startswith('=>',end):break
   end+=1
  sig=' '.join(body[pos:end].split()).replace('async ','')
  desc=re.sub('<[^>]+>','', ' '.join(comments)).replace('&lt;','<').replace('&gt;','>');comments=[]
  if sig:
   entries.append((sig,desc))
  if body.startswith('=>',end):pos=body.find(';',end)+1
  elif end<len(body) and body[end]=='{':pos=closing(body,end)+1
  else:pos=end+1
 page=[f'# {label}（{interface}）','',f'命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。','']
 if interface=='IQOfficialMessageStream':page+=['由 `IQOfficialDirectMessageApi.BeginStream` 返回；使用完调用 `DisposeAsync` 或 `await using`。','']
 else:page+=['```csharp',f'var api = Context.GetAdapterExtension<{interface}>();','if (api is null) return;','```','', '接口可获取不表示每个方法都已实现；未实现的操作会抛出 `NotSupportedException`。平台权限与参数错误仍可能导致调用失败。','']
 if interface=='IQGroupApi':page+=['先检查 `Capabilities`。官方与 Milky 的 ID 都为字符串，但各自只在来源实例内有意义。入群审批传原申请对象，分页持续使用 NextCursor，即使当前页 Requests 为空。','']
 for sig,desc in entries:
  if '(' not in sig:
   prop=sig.split()[-1]; page +=[f'## {prop}','',desc or '当前服务状态或实现能力。','','```csharp',sig+' { get; }','```',''];continue
  method=re.search(r'\b(\w+)\(',sig); assert method, sig
  name=method.group(1); begin=method.end()-1; ret=sig[:method.start()].strip();params=sig[begin+1:sig.rfind(')')]
  page +=[f'## {name}','',desc or actions.get(name, f'调用 `{interface}.{name}`。'),'','```csharp',(ret+' '+name+'(\n    '+',\n    '.join(split_params(params))+'\n);' if len(sig)>100 else sig+';'),'```','']
  if params:
   page+=['### 参数','','| 参数 | C# 类型 | 默认值 | 说明 |','| --- | --- | --- | --- |']
   for p in split_params(params):
    parts=p.split(' = ',1);decl=parts[0];typ,n=decl.rsplit(' ',1);default=parts[1] if len(parts)>1 else '必传'
    explanation=meaning[n]
    if n=='reply' and '?' not in typ:explanation='必传的被动回复凭据，来自消息或事件。'
    page.append(f'| `{n}` | `{typ}` | `{default}` | {explanation} |')
   page+=['']
  page+=['### 返回','','`'+ret+'`。'+ ('异步完成后不返回业务值。' if ret in ['Task','ValueTask'] else return_meanings.get(name, '返回发送后的消息 ID。' if ret=='Task<string>' and (name.startswith('Send') or name.startswith('UploadAndSend')) else '返回上传文件的 ID。' if name.startswith('Upload') else '返回创建的文件夹 ID。' if name=='CreateGroupFolderAsync' else '返回下载链接。' if 'DownloadUrl' in name or name=='GetResourceTempUrlAsync' else '返回完成后的消息 ID。' if name=='CompleteAsync' else '等待异步完成后取得签名所列模型或列表。') if ret.startswith(('Task<','ValueTask<')) else return_meanings.get(name,'同步返回结果。')),'']
  count+=1
 if interface=='IQGroupApi':page+=['## 入群审批示例','','```csharp','Events.MapPlatform(QEventKinds.GroupJoinRequest, async evt =>','{','    if (evt.Raw is not QGroupJoinRequest request) return;','    var groups = Context.GetAdapterExtension<IQGroupApi>();','    if (groups?.Capabilities.HasFlag(QGroupCapabilities.JoinRequests) == true)','        await groups.AcceptJoinRequestAsync(request);','});','```','']
 if interface=='IQOfficialMessageStream':page+=['## DisposeAsync（继承自 IAsyncDisposable）','','释放会话资源。建议使用 `await using`，是否取消未完成回复由适配器实现决定。','','```csharp','ValueTask DisposeAsync();','```','']
 page+=['## 相关类型','','见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。','']
 (out/f'{slug}.md').write_text('\n'.join(page))
# Keep model declarations exactly as source, including enum values, optional fields and helpers.
models=[('QEntities.cs','实体与枚举'),('QGroupManagement.cs','群管理与策略'),('QMessages.cs','原生消息'),('QSegments.cs','入站与出站消息段'),('QEvents.cs','事件与事件 Kind'),('QOfficialMessages.cs','官方消息、Markdown、按钮与媒体')]
page=['# QQ C# 类型参考','','命名空间：`ShiroBot.Model.QQ`。以下完整声明对应 QQ Model ABI `1.0.0.0`。','','`required` 属性必须初始化；`init` 属性在创建对象时设置；位置式 record 使用所列构造参数。','ID 与分页游标均为不透明字符串。文件大小、计数、时间及枚举保持各自类型。','继承类型同时拥有基类属性。消息、事件和申请中的 ID 应交回产生它们的适配器实例。','']
for file,label in models:
 src=(root/'Models/QQ'/file).read_text();page +=[f'## {label}','']
 for decl in re.finditer(r'^public (?:(?:abstract|sealed|static|readonly) )?(?:record(?: struct)?|enum|class|struct) (\w+)',src,re.M):
  start=decl.start(); i=decl.end(); depth=0
  while i<len(src):
   c=src[i]
   if c=='(':depth+=1
   elif c==')':depth-=1
   elif depth==0 and c in '{;':break
   i+=1
  end=closing(src,i)+1 if src[i]=='{' else i+1
  page +=[f'### {decl.group(1)}','','```csharp',src[start:end],'```','']
# Capability enum lives beside strategy interface.
src=(root/'Models/QQ/IQGroupApprovalStrategyApi.cs').read_text();page+=['## 群管理能力标记','','```csharp',src[src.index('[Flags]'):].strip(),'```','']
(out/'types.md').write_text('\n'.join(page))
index=['# QQ C# 接口参考','','这里按插件调用方式记录全部 QQ Model 扩展接口，包含 C# 方法签名、参数、默认值与返回类型。','无需构造 HTTP 请求。插件通过 `Context.GetAdapterExtension<T>()` 获取当前实例的服务。','','SDK 与 QQ Model ABI 均为 **1.0.0.0**；所有引用旧 ABI 的组件需重新编译。','所有用户、群、消息、申请 ID 均使用字符串。数值字符串与 OpenID 仍属于各自的来源实例。','','## 服务接口','','| 分类 | C# 接口 | 文档 |','| --- | --- | --- |']
for interface,slug,label in services:index.append(f'| {label} | `{interface}` | [{label}](./qq/{slug}) |')
index+=['','## 模型与迁移','','- [完整 C# 类型：实体、消息、消息段、事件、按钮和策略](./qq/types)','- [ABI 变更及迁移表](./qq-interface-review)','- [官方 Markdown 与按钮使用示例](./qq-official)','','## 适配器支持','','群管理通过 `IQGroupApi.Capabilities` 检查具体操作。其他扩展先检查服务是否为 null；','拿到服务后，未实现的方法仍可能抛出 `NotSupportedException`。','官方自动审批策略使用独立的 `IQGroupApprovalStrategyApi`；平台权限由 QQ 管理。','']
(root/'docs/plugin/qq-reference.md').write_text('\n'.join(index))
print(f'Generated {len(services)} interface pages, {count} methods and complete model declarations.')
