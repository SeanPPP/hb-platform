import type { PreorderActivationDetail } from "./types";

export function preorderActivationQueryKey(activationGuid: string, storeCode: string) {
  return ["preorder", "activation", activationGuid, storeCode] as const;
}

function orderStatusPriority(status: PreorderActivationDetail["orderStatus"]) {
  switch (status) {
    case undefined:
    case "Draft":
      return 0;
    case "ReturnedForRevision":
      return 1;
    case "Processing":
      return 3;
    case "Completed":
    case "Cancelled":
      return 4;
    case "Submitted":
    case "NoDemand":
    default:
      // 未知新状态按已提交事实 fail-closed，不能被可编辑响应降级。
      return 2;
  }
}

function normalizedOrderStatus(status: PreorderActivationDetail["orderStatus"]) {
  return status ?? "Draft";
}

/** 缓存先按 revision 单调前进；同 revision 再按订单状态语义防止晚响应回退。 */
export function mergePreorderDraftCacheDetail(
  current: PreorderActivationDetail | undefined,
  incoming: PreorderActivationDetail
) {
  if (!current) return incoming;
  if (current.draftRevision > incoming.draftRevision) return current;
  if (current.draftRevision < incoming.draftRevision) return incoming;

  const currentPriority = orderStatusPriority(current.orderStatus);
  const incomingPriority = orderStatusPriority(incoming.orderStatus);
  if (currentPriority > incomingPriority) return current;
  if (currentPriority < incomingPriority) return incoming;
  if (normalizedOrderStatus(current.orderStatus) !== normalizedOrderStatus(incoming.orderStatus)) {
    // Submitted/NoDemand 等互斥同级事实不凭响应先后互相覆盖。
    return current;
  }
  return incoming;
}
