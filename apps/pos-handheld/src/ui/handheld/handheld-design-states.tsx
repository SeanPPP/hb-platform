import type { ReactNode } from "react";
import { View, type StyleProp, type ViewStyle } from "react-native";

export const handheldDesignStates = [
  { id: "01", slug: "startup" },
  { id: "02", slug: "device-registration" },
  { id: "03", slug: "registration-states" },
  { id: "04", slug: "online-login" },
  { id: "05", slug: "offline-login" },
  { id: "06", slug: "operation-authorization" },
  { id: "07", slug: "sales-empty" },
  { id: "08", slug: "sales-active" },
  { id: "09", slug: "product-search" },
  { id: "10", slug: "cart-item-actions" },
  { id: "11", slug: "open-item-keypad" },
  { id: "12", slug: "camera-scanner" },
  { id: "13", slug: "sales-more-actions" },
  { id: "14", slug: "payment-method" },
  { id: "15", slug: "cash-payment" },
  { id: "16", slug: "card-processing" },
  { id: "17", slug: "payment-success" },
  { id: "18", slug: "payment-failure" },
  { id: "19", slug: "held-orders-list" },
  { id: "20", slug: "held-order-detail" },
  { id: "21", slug: "returns-lookup" },
  { id: "22", slug: "return-confirmation" },
  { id: "23", slug: "local-history-list" },
  { id: "24", slug: "local-history-detail" },
  { id: "25", slug: "remote-history-list" },
  { id: "26", slug: "remote-history-detail" },
  { id: "27", slug: "sync-history" },
  { id: "28", slug: "sync-detail" },
  { id: "29", slug: "installments-list" },
  { id: "30", slug: "installment-detail" },
  { id: "31", slug: "daily-close-count" },
  { id: "32", slug: "daily-close-summary" },
  { id: "33", slug: "special-products-grid" },
  { id: "34", slug: "special-product-editor" },
  { id: "35", slug: "catalog-maintenance" },
  { id: "36", slug: "attendance-qr" },
  { id: "37", slug: "attendance-audit" },
  { id: "38", slug: "settings-index" },
  { id: "39", slug: "peripheral-settings" },
  { id: "40", slug: "transaction-settings" },
  { id: "41", slug: "required-update" },
  { id: "42", slug: "update-recovery" },
  { id: "43", slug: "pda-scan-ready" },
  { id: "44", slug: "pda-scan-result" },
  { id: "45", slug: "pda-printer-connect" },
  { id: "46", slug: "pda-print-drawer-result" },
] as const;

export type HandheldDesignStateSlug =
  (typeof handheldDesignStates)[number]["slug"];

type HandheldStateSurfaceProps = Readonly<{
  children: ReactNode;
  slug: HandheldDesignStateSlug;
  style?: StyleProp<ViewStyle>;
}>;

/** 为每个可视状态提供稳定定位点，供交互测试与截图验收共用。 */
export function HandheldStateSurface({
  children,
  slug,
  style,
}: HandheldStateSurfaceProps) {
  return (
    <View style={style} testID={`handheld-state-${slug}`}>
      {children}
    </View>
  );
}
