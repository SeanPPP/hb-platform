import { CodeTableCard } from "@/components/product-maintenance/CodeTableCard";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { ProductSetCodeItem } from "@/modules/product-maintenance/types";

interface SetCodeCompactSectionProps {
  items: ProductSetCodeItem[];
  savingItemId?: string | null;
  printingItemId?: string | null;
  /** 相对基线有改动的套装行 setCodeId。 */
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

export function SetCodeCompactSection({
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
}: SetCodeCompactSectionProps) {
  const { t } = useAppTranslation("productQuery");

  return (
    <CodeTableCard
      title={t("setCode.title")}
      priceColumnLabel={t("codes.setPriceColumn")}
      loadingText={t("setCode.loading")}
      rows={items.map((item) => ({
        id: item.setCodeId,
        barcode: item.setBarcode,
        price: item.setRetailPrice == null ? null : item.setRetailPrice.toFixed(2),
        dirty: dirtyItemIds?.has(item.setCodeId) ?? false,
      }))}
      totalCount={totalCount}
      savingItemId={savingItemId}
      printingItemId={printingItemId}
      adding={savingItemId === "new-set"}
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
