namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Domain;

internal static class StoreOrderLifecycleRules
{
    internal const int Draft = 0;
    internal const int Submitted = 1;
    internal const int Completed = 2;
    internal const int Picking = 3;

    internal static StoreOrderLifecycleFailure? ValidateStatusTarget(int targetStatus)
    {
        return targetStatus is Submitted or Completed
            ? null
            : new StoreOrderLifecycleFailure(
                null,
                "Invalid status. Only 1 (Submitted) or 2 (Completed) are allowed."
            );
    }

    internal static StoreOrderLifecycleFailure? ValidateComplete(int? currentStatus)
    {
        return currentStatus == Submitted
            ? null
            : new StoreOrderLifecycleFailure(
                null,
                "只有已提交状态的订单才能标记为完成"
            );
    }

    internal static bool IsStartPickingAlreadySatisfied(int? currentStatus)
    {
        return currentStatus is Completed or Picking;
    }

    internal static StoreOrderLifecycleFailure? ValidateStartPicking(int? currentStatus)
    {
        return currentStatus == Submitted
            ? null
            : new StoreOrderLifecycleFailure(
                null,
                "只有已提交状态的订单才能开始配货"
            );
    }

    internal static StoreOrderLifecycleFailure? ValidateStatusTransition(
        int? currentStatus,
        int targetStatus,
        bool canBypassPreorderGate
    )
    {
        if (targetStatus == Submitted)
        {
            if (currentStatus == Completed || (currentStatus == Draft && canBypassPreorderGate))
            {
                return null;
            }

            if (currentStatus == Draft)
            {
                return new StoreOrderLifecycleFailure(
                    "PREORDER_SUBMIT_ENDPOINT_REQUIRED",
                    "草稿订单必须通过正式提交接口提交，以完成 Preorder 门禁检查"
                );
            }
        }
        else if (targetStatus == Completed && currentStatus == Submitted)
        {
            return null;
        }

        return new StoreOrderLifecycleFailure(
            "INVALID_ORDER_STATUS_TRANSITION",
            currentStatus == Draft && targetStatus == Completed
                ? "草稿订单不能直接标记为已完成"
                : "当前订单状态不允许切换到目标状态"
        );
    }
}
