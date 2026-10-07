using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Adapter;

/// <summary>确认点击被接收，不代表业务操作完成。适配器自动确认时再次调用应为幂等操作。</summary>
public interface IMessageInteractionService
{
    Task AcknowledgeAsync(InteractionEvent interaction, CancellationToken cancellationToken = default);
}
/// <summary>可选表情回应服务；插件应使用宿主返回的实例路由包装器。</summary>
public interface IMessageReactionService
{
    ReactionCapabilities GetReactionCapabilities(Channel channel);
    Task SetReactionAsync(MessageReference message, ReactionEmoji emoji, bool isAdd = true, CancellationToken cancellationToken = default);
}
