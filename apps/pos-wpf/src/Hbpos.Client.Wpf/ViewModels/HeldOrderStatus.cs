using Hbpos.Client.Wpf.Models;
using HeldServerStatus = Hbpos.Contracts.HeldOrders.SharedHeldOrderStatus;

namespace Hbpos.Client.Wpf.ViewModels;

/// <summary>
/// 共享挂单行的展示徽标：本地挂单（未评估/旧挂单）、本地待发布、已发布、
/// 远端可取、本机 Prepared/Active claim、他端认领、已完成、本地阻断。
/// 徽标决定列表状态颜色与 StatusLabel，Prepared/Active 必须来自本地 durable claim。
/// </summary>
public enum HeldOrderBadgeKind
{
    LocalHold = 0,
    LocalPendingPublish = 1,
    Published = 2,
    RemotePending = 3,
    LocalClaimPrepared = 4,
    LocalClaimActive = 5,
    ClaimedByOther = 6,
    Completed = 7,
    Blocked = 8
}

public static class HeldOrderStatusResolver
{
    /// <summary>
    /// 解析合并后的展示状态，优先级：
    /// 本机 claim（Prepared/Active）&gt; 本地阻断 &gt; 服务端已完成 &gt; 他端认领
    /// &gt; 服务端 Pending（本地待发布时显示待发布，否则远端可取）
    /// &gt; 本地发布状态（待发布/已发布）&gt; 本地普通挂单。
    /// </summary>
    public static HeldOrderBadgeKind Resolve(
        SharedHeldOrderPublicationStatus? publicationStatus,
        HeldServerStatus? serverStatus,
        SharedHeldOrderClaimStatus? localClaimStatus)
    {
        if (localClaimStatus == SharedHeldOrderClaimStatus.Prepared)
        {
            return HeldOrderBadgeKind.LocalClaimPrepared;
        }

        if (localClaimStatus == SharedHeldOrderClaimStatus.Active)
        {
            return HeldOrderBadgeKind.LocalClaimActive;
        }

        if (publicationStatus == SharedHeldOrderPublicationStatus.Blocked)
        {
            return HeldOrderBadgeKind.Blocked;
        }

        if (serverStatus == HeldServerStatus.Completed)
        {
            return HeldOrderBadgeKind.Completed;
        }

        if (serverStatus == HeldServerStatus.Claimed)
        {
            return HeldOrderBadgeKind.ClaimedByOther;
        }

        if (serverStatus == HeldServerStatus.Pending)
        {
            return publicationStatus == SharedHeldOrderPublicationStatus.PendingPublish
                ? HeldOrderBadgeKind.LocalPendingPublish
                : HeldOrderBadgeKind.RemotePending;
        }

        return publicationStatus switch
        {
            SharedHeldOrderPublicationStatus.PendingPublish => HeldOrderBadgeKind.LocalPendingPublish,
            SharedHeldOrderPublicationStatus.Published => HeldOrderBadgeKind.Published,
            _ => HeldOrderBadgeKind.LocalHold
        };
    }
}
