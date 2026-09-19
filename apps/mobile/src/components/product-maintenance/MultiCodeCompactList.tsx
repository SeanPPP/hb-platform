import { CodeTableCard } from "@/components/product-maintenance/CodeTableCard";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { MultiCodeEditableItem } from "@/modules/product-maintenance/types";

interface MultiCodeCompactListProps {
  items: MultiCodeEditableItem[];
  savingItemId?: string | null;
  printingItemId?: string | null;
  /** 相对基线有改动的行 id（setCodeId，历史多码为 UUID）。 */
  dirtyItemIds?: ReadonlySet<string>;
  totalCount?: number;
  loading?: boolean;
  loadingMore?: boolean;
  hasMore?: boolean;
  onEditItemBarcode: (setCodeId: string) => void;
  onEditItemRetailPrice: (setCodeId: string) => void;
  onSaveItem: (setCodeId: string) => void;
  onPrintItem: (setCodeId: string) => void;
  onAddItem: () => void;
  onLoadMore?: () => void;
}

export function MultiCodeCompactList({
  items,
  savingItemId,
  printingItemId,
  dirtyItemIds,
  totalCount,
  loading,
  loadingMore,
  hasMore,
  onEditItemBarcode,
  onEditItemRetailPrice,
  onSaveItem,
  onPrintItem,
  onAddItem,
  onLoadMore,
}: MultiCodeCompactListProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <CodeTableCard
      title={t("multiCode.title")}
      priceColumnLabel={t("codes.retailColumn")}
      loadingText={t("multiCode.loading")}
      rows={items.map((item) => {
        // 历史多码可能没有 setCodeId，交互必须稳定地回传 UUID，不能误命中首行。
        const itemId = item.setCodeId || item.uuid;
        return {
          id: itemId,
          barcode: item.barcode,
          price: item.retailPrice == null ? null : item.retailPrice.toFixed(2),
          // 多码零售价为空时打印沿用主条码价，界面显示为「跟随」。
          followsMain: item.retailPrice == null,
          dirty: dirtyItemIds?.has(itemId) ?? false,
        };
      })}
      totalCount={totalCount}
      savingItemId={savingItemId}
      printingItemId={printingItemId}
      adding={savingItemId === "new-multi"}
      loading={loading}
      loadingMore={loadingMore}
      hasMore={hasMore}
      onEditItemBarcode={onEditItemBarcode}
      onEditItemRetailPrice={onEditItemRetailPrice}
      onSaveItem={onSaveItem}
      onPrintItem={onPrintItem}
      onAddItem={onAddItem}
      onLoadMore={onLoadMore}
    />
  );
}
