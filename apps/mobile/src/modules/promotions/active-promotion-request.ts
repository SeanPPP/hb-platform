import type { PromotionListItem } from "./types";

export interface ActivePromotionRequestContext {
  productCode: string;
  storeCode: string;
}

interface ActivePromotionRequestCoordinatorOptions {
  fetchPromotions: (
    productCode: string,
    storeCode: string,
  ) => Promise<PromotionListItem[]>;
  applyPromotions: (items: PromotionListItem[]) => void;
  onFailure?: (error: unknown, context: ActivePromotionRequestContext) => void;
}

export function createActivePromotionRequestCoordinator({
  fetchPromotions,
  applyPromotions,
  onFailure,
}: ActivePromotionRequestCoordinatorOptions) {
  let latestRequestId = 0;

  return {
    invalidate() {
      latestRequestId += 1;
      applyPromotions([]);
    },
    async load(productCode: string, storeCode: string) {
      const requestId = ++latestRequestId;
      try {
        const items = await fetchPromotions(productCode, storeCode);
        // 只允许最后一次商品/分店请求更新界面，避免连续扫码的迟到响应串数据。
        if (requestId === latestRequestId) {
          applyPromotions(items);
        }
      } catch (error) {
        if (requestId === latestRequestId) {
          applyPromotions([]);
          onFailure?.(error, { productCode, storeCode });
        }
      }
    },
  };
}
