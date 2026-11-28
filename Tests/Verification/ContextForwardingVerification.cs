using System.Reflection;
using ShiroBot.Hosting.Context;
using ShiroBot.SDK.Adapter;

namespace ShiroBot.Verification;

/// <summary>
///     宿主包装器必须显式转发 SDK 服务的每个成员。漏掉的成员仍能编译，但会落到接口默认实现，
///     对插件表现为“不支持”，即使当前适配器已经实现了它。
/// </summary>
internal static class ContextForwardingVerification
{
    public static void Run()
    {
        AssertForwardsAll(typeof(MessageContext), typeof(IMessageService));
        AssertForwardsAll(typeof(BotContext).GetNestedType("SwitchableChannelService", BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException("SwitchableChannelService not found."),
            typeof(IChannelService));
    }

    private static void AssertForwardsAll(Type implementation, Type service)
    {
        var map = implementation.GetInterfaceMap(service);
        var missing = map.TargetMethods
            .Where(method => method.DeclaringType == service)
            .Select(method => method.Name)
            .Distinct()
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"{implementation.Name} does not forward {service.Name} members: {string.Join(", ", missing)}.");
    }
}
