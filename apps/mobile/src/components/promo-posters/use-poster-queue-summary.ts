import { useMemo } from "react";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { countPosterPages, groupPostersBySize } from "@/modules/promo-posters/logic";
import { usePromoPosterQueueHydration, usePromoPosterQueueStore } from "@/modules/promo-posters/queue-store";

/** 待打印汇总：「A6 × 3 · A4 × 1 · 拼版后 2 页 A4」，供扫码页浮条与待打印页头共用。 */
export function usePosterQueueSummary() {
  const { t } = useAppTranslation(["productQuery"]);
  usePromoPosterQueueHydration();
  const items = usePromoPosterQueueStore((state) => state.items);
  const impose = usePromoPosterQueueStore((state) => state.impose);

  return useMemo(() => {
    const groups = groupPostersBySize(items);
    const pages = countPosterPages(
      items.map((item) => item.poster.size),
      impose,
    );
    const pagesText = impose
      ? t("poster.summary.imposedPages", { count: pages })
      : t("poster.summary.pages", { count: pages });
    const sizes = groups.map((group) => t("poster.summary.sizeCount", { size: group.size, count: group.count }));
    return {
      count: items.length,
      pages,
      pagesText,
      detail: [...sizes, pagesText].join(" · "),
    };
  }, [impose, items, t]);
}
