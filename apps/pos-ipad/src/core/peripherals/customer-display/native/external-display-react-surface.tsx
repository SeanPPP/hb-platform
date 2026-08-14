import type { TFunction } from "i18next";
import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AppRegistry, StyleSheet, Text, View } from "react-native";

import {
  CUSTOMER_DISPLAY_VISIBLE_ITEM_LIMIT,
  CustomerDisplaySnapshotSchema,
  type CustomerDisplaySnapshot,
} from "../../../contracts/external-display";

import type { ExternalDisplayNativeModule } from "./external-display-bridge";

const surfaceModuleName = "HBExternalDisplay";

type ExternalDisplaySurfaceProps = {
  surfaceId: string;
  snapshot: CustomerDisplaySnapshot | null;
};

type DisplaySummary = Readonly<{
  itemQuantity: string;
  skuCount: number;
  subtotal: CustomerDisplaySnapshot["total"];
}>;

type StatusCopy = Readonly<{
  title: string;
  subtitle: string;
}>;

type VisibleItemWindow = Readonly<{
  start: number;
  items: CustomerDisplaySnapshot["items"];
  hiddenBefore: number;
  hiddenAfter: number;
}>;

let registeredNativeModule: ExternalDisplayNativeModule | null = null;

function parseSnapshot(value: unknown): CustomerDisplaySnapshot | null {
  const result = CustomerDisplaySnapshotSchema.safeParse(value);
  return result.success ? result.data : null;
}

function formatMoney(money: CustomerDisplaySnapshot["total"]): string {
  const absoluteCents = Math.abs(money.cents);
  const sign = money.cents < 0 ? "-" : "";
  return `${sign}$${Math.floor(absoluteCents / 100)}.${String(
    absoluteCents % 100,
  ).padStart(2, "0")}`;
}

function formatDiscount(money: CustomerDisplaySnapshot["discount"]): string {
  const absoluteCents = Math.abs(money.cents);
  if (absoluteCents === 0) {
    return "$0.00";
  }

  // snapshot 保存的是折扣绝对金额；客显必须明确呈现为减项。
  return `−$${Math.floor(absoluteCents / 100)}.${String(
    absoluteCents % 100,
  ).padStart(2, "0")}`;
}

function summarizeSnapshot(
  snapshot: CustomerDisplaySnapshot | null,
): DisplaySummary {
  if (snapshot === null) {
    return {
      itemQuantity: "0",
      skuCount: 0,
      subtotal: { currency: "AUD", cents: 0 },
    };
  }
  if (snapshot.summary !== undefined) {
    return snapshot.summary;
  }

  // 旧版原生快照没有 summary；只使用既有白名单字段恢复等价显示。
  return {
    itemQuantity: sumFixedQuantities(
      snapshot.items.map((item) => item.quantity),
    ),
    skuCount: snapshot.items.length,
    subtotal: {
      currency: "AUD",
      cents: snapshot.total.cents + snapshot.discount.cents,
    },
  };
}

function sumFixedQuantities(quantities: readonly string[]): string {
  let totalThousandths = 0n;

  for (const quantity of quantities) {
    const match = /^(-?)(\d+)(?:\.(\d{1,3}))?$/.exec(quantity);
    if (match === null) continue;
    const whole = BigInt(match[2]!);
    const fraction = BigInt((match[3] ?? "").padEnd(3, "0") || "0");
    const value = whole * 1_000n + fraction;
    totalThousandths += match[1] === "-" ? -value : value;
  }

  const sign = totalThousandths < 0n ? "-" : "";
  const absolute = totalThousandths < 0n ? -totalThousandths : totalThousandths;
  const fraction = String(absolute % 1_000n)
    .padStart(3, "0")
    .replace(/0+$/, "");
  return `${sign}${absolute / 1_000n}${
    fraction.length > 0 ? `.${fraction}` : ""
  }`;
}

function getStatusCopy(
  snapshot: CustomerDisplaySnapshot | null,
  translate: TFunction,
): StatusCopy {
  if (snapshot === null) {
    return {
      title: translate("customerDisplay.status.idle.title"),
      subtitle: translate("customerDisplay.status.idle.subtitle"),
    };
  }

  switch (snapshot.mode) {
    case "idle":
      return {
        title: translate("customerDisplay.status.idle.title"),
        subtitle: translate("customerDisplay.status.idle.subtitle"),
      };
    case "cart":
      return {
        title: translate("customerDisplay.status.cart.title"),
        subtitle: translate("customerDisplay.status.cart.subtitle"),
      };
    case "payment":
      return {
        title: translate("customerDisplay.status.payment.title"),
        subtitle: translate("customerDisplay.status.payment.subtitle"),
      };
    case "change":
      return {
        title: translate("customerDisplay.status.change.title"),
        subtitle: formatMoney(snapshot.change),
      };
    case "success": {
      const hasChange = snapshot.change.cents !== 0;
      return {
        title: translate("customerDisplay.status.success.title"),
        subtitle: hasChange
          ? translate("customerDisplay.status.success.change", {
              amount: formatMoney(snapshot.change),
            })
          : translate("customerDisplay.status.success.thankYou"),
      };
    }
  }
}

function visibleItemWindow(
  snapshot: CustomerDisplaySnapshot | null,
): VisibleItemWindow {
  if (snapshot === null || snapshot.items.length === 0) {
    return { start: 0, items: [], hiddenBefore: 0, hiddenAfter: 0 };
  }
  const maximumStart = Math.max(
    0,
    snapshot.items.length - CUSTOMER_DISPLAY_VISIBLE_ITEM_LIMIT,
  );
  const start = Math.max(
    0,
    Math.min(snapshot.visibleItemStart ?? maximumStart, maximumStart),
  );
  const items = snapshot.items.slice(
    start,
    start + CUSTOMER_DISPLAY_VISIBLE_ITEM_LIMIT,
  );
  return {
    start,
    items,
    hiddenBefore: start,
    hiddenAfter: Math.max(0, snapshot.items.length - start - items.length),
  };
}

function hiddenItemsText(
  window: VisibleItemWindow,
  translate: TFunction,
): string | null {
  if (window.hiddenBefore > 0 && window.hiddenAfter > 0) {
    return translate("customerDisplay.hidden.both", {
      before: window.hiddenBefore,
      after: window.hiddenAfter,
    });
  }
  if (window.hiddenBefore > 0) {
    return translate("customerDisplay.hidden.before", {
      count: window.hiddenBefore,
    });
  }
  if (window.hiddenAfter > 0) {
    return translate("customerDisplay.hidden.after", {
      count: window.hiddenAfter,
    });
  }
  return null;
}

export function ExternalDisplaySurface({
  surfaceId,
  snapshot: initialSnapshot,
}: ExternalDisplaySurfaceProps) {
  // 客显面向顾客固定使用英文，不跟随收银主界面的语言切换。
  const { t } = useTranslation(undefined, { lng: "en" });
  const [snapshot, setSnapshot] = useState(() =>
    parseSnapshot(initialSnapshot),
  );

  useEffect(() => {
    const nativeModule = registeredNativeModule;
    if (nativeModule === null) {
      return;
    }

    const subscription = nativeModule.addListener(
      "onSnapshotChanged",
      (nextSnapshot) => {
        const parsed = parseSnapshot(nextSnapshot);
        if (parsed !== null) {
          setSnapshot((currentSnapshot) =>
            currentSnapshot === null ||
            parsed.revision > currentSnapshot.revision
              ? parsed
              : currentSnapshot,
          );
        }
      },
    );
    void nativeModule.markReactSurfaceRendered(surfaceId).catch(() => {
      // Swift 会保持 UIKit 占位并发送 failed，不影响主收银界面。
    });

    return () => subscription.remove();
  }, [surfaceId]);

  const itemWindow = useMemo(() => visibleItemWindow(snapshot), [snapshot]);
  const summary = useMemo(() => summarizeSnapshot(snapshot), [snapshot]);
  const status = getStatusCopy(snapshot, t);
  const overflowText = hiddenItemsText(itemWindow, t);
  const showsFullScreenAdvert =
    snapshot !== null &&
    snapshot.mode === "idle" &&
    snapshot.items.length === 0 &&
    snapshot.advert !== null;

  if (showsFullScreenAdvert) {
    return (
      <View
        accessible={false}
        pointerEvents="none"
        style={styles.surface}
        testID="external-display-surface"
      >
        <View
          style={styles.fullScreenNativeAdvertWindow}
          testID="external-display-advert-window"
        />
      </View>
    );
  }

  return (
    <View
      accessible={false}
      pointerEvents="none"
      style={styles.surface}
      testID="external-display-surface"
    >
      <View style={styles.titleBar} testID="external-display-title-bar">
        <Text style={styles.title}>{t("customerDisplay.title")}</Text>
      </View>

      <View style={styles.content}>
        <View style={styles.upperPanels}>
          <View
            style={styles.transactionPanel}
            testID="external-display-transaction-panel"
          >
            <View
              style={styles.orderHeading}
              testID="external-display-order-heading"
            >
              <Text numberOfLines={1} style={styles.orderTitle}>
                {t("customerDisplay.orderTitle")}
              </Text>
              {overflowText === null ? null : (
                <Text
                  numberOfLines={1}
                  style={styles.hiddenItems}
                  testID="external-display-hidden-items"
                >
                  {overflowText}
                </Text>
              )}
            </View>

            <View style={styles.tableHeader}>
              <Text style={[styles.headerText, styles.productColumn]}>
                {t("customerDisplay.column.product")}
              </Text>
              <Text style={[styles.headerText, styles.quantityColumn]}>
                {t("customerDisplay.column.quantity")}
              </Text>
              <Text style={[styles.headerText, styles.moneyColumn]}>
                {t("customerDisplay.column.unitPrice")}
              </Text>
              <Text style={[styles.headerText, styles.moneyColumn]}>
                {t("customerDisplay.column.amount")}
              </Text>
            </View>

            <View style={styles.items}>
              {itemWindow.items.length === 0 ? (
                <Text style={styles.empty}>{t("customerDisplay.empty")}</Text>
              ) : (
                itemWindow.items.map((item, index) => (
                  <View
                    key={`${itemWindow.start + index}-${item.name}`}
                    style={styles.itemRow}
                    testID="external-display-item-row"
                  >
                    <Text
                      numberOfLines={1}
                      style={[styles.itemText, styles.productColumn]}
                    >
                      {item.name}
                    </Text>
                    <Text style={[styles.secondaryText, styles.quantityColumn]}>
                      × {item.quantity}
                    </Text>
                    <Text style={[styles.itemText, styles.moneyColumn]}>
                      {item.unitPrice === undefined
                        ? "—"
                        : formatMoney(item.unitPrice)}
                    </Text>
                    <Text style={[styles.itemTextStrong, styles.moneyColumn]}>
                      {formatMoney(item.amount)}
                    </Text>
                  </View>
                ))
              )}
            </View>
          </View>

          {/* 该透明窗口由 UIKit 本地媒体层承载，不在 React 上叠加广告文案。 */}
          <View
            style={styles.nativeAdvertWindow}
            testID="external-display-advert-window"
          />
        </View>

        <View
          style={styles.summaryPanel}
          testID="external-display-summary-panel"
        >
          <View
            style={styles.summaryMetrics}
            testID="external-display-summary-metrics"
          >
            <Text style={styles.summaryCounts}>
              {t("customerDisplay.summary.counts", {
                itemQuantity: summary.itemQuantity,
                skuCount: summary.skuCount,
              })}
            </Text>
            <View style={styles.metricRow}>
              <Metric
                label={t("customerDisplay.summary.subtotal")}
                value={formatMoney(summary.subtotal)}
              />
              <View style={styles.metricSeparator} />
              <Metric
                label={t("customerDisplay.gst")}
                value={
                  snapshot === null ? "$0.00" : formatMoney(snapshot.gst)
                }
              />
              <View style={styles.metricSeparator} />
              <Metric
                label={t("customerDisplay.discount")}
                value={
                  snapshot === null
                    ? "$0.00"
                    : formatDiscount(snapshot.discount)
                }
              />
            </View>
          </View>

          <View
            style={styles.amountDue}
            testID="external-display-amount-due"
          >
            <Text style={styles.amountDueLabel}>
              {t("customerDisplay.summary.amountDue")}
            </Text>
            <Text style={styles.amountDueValue}>
              {snapshot === null ? "$0.00" : formatMoney(snapshot.total)}
            </Text>
          </View>

          <View
            style={styles.statusRegion}
            testID="external-display-status-region"
          >
            <View
              style={styles.statusCard}
              testID="external-display-status-card"
            >
              <Text style={styles.statusTitle}>{status.title}</Text>
              <Text numberOfLines={2} style={styles.statusSubtitle}>
                {status.subtitle}
              </Text>
            </View>
          </View>
        </View>
      </View>
    </View>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.metric}>
      <Text style={styles.metricLabel}>{label}</Text>
      <Text style={styles.metricValue}>{value}</Text>
    </View>
  );
}

export function registerExternalDisplayReactSurface(
  nativeModule: ExternalDisplayNativeModule | null,
) {
  registeredNativeModule = nativeModule;

  if (!AppRegistry.getAppKeys().includes(surfaceModuleName)) {
    AppRegistry.registerComponent(
      surfaceModuleName,
      () => ExternalDisplaySurface,
    );
  }

  if (nativeModule !== null) {
    void nativeModule.markReactSurfaceReady().catch(() => {
      // 无 Development Build 或原生 factory 未就绪时保持 disconnected/failed。
    });
  }
}

const styles = StyleSheet.create({
  surface: {
    flex: 1,
    flexDirection: "column",
    backgroundColor: "transparent",
  },
  fullScreenNativeAdvertWindow: {
    flex: 1,
    backgroundColor: "transparent",
  },
  titleBar: {
    height: 48,
    justifyContent: "center",
    paddingHorizontal: 24,
    backgroundColor: "#071426",
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "rgba(255,255,255,0.22)",
  },
  title: {
    color: "#ffffff",
    fontSize: 21,
    fontWeight: "700",
  },
  content: {
    flex: 1,
    gap: 18,
    padding: 24,
  },
  upperPanels: {
    minHeight: 0,
    flex: 1,
    flexDirection: "row",
    gap: 18,
  },
  transactionPanel: {
    flex: 1,
    overflow: "hidden",
    padding: 16,
    backgroundColor: "#071426",
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.24)",
    borderRadius: 12,
  },
  nativeAdvertWindow: {
    flex: 1,
    overflow: "hidden",
    backgroundColor: "transparent",
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.24)",
    borderRadius: 12,
  },
  orderHeading: {
    minHeight: 36,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: 12,
  },
  orderTitle: {
    flexShrink: 1,
    color: "#72e6c3",
    fontSize: 30,
    fontWeight: "700",
  },
  hiddenItems: {
    marginLeft: 12,
    color: "rgba(255,255,255,0.58)",
    fontSize: 14,
    fontWeight: "500",
    textAlign: "right",
  },
  tableHeader: {
    minHeight: 38,
    flexDirection: "row",
    alignItems: "center",
    borderBottomWidth: 1,
    borderBottomColor: "rgba(255,255,255,0.28)",
  },
  headerText: {
    color: "rgba(255,255,255,0.88)",
    fontSize: 16,
    fontWeight: "600",
  },
  productColumn: {
    flex: 1,
    paddingRight: 12,
  },
  quantityColumn: {
    width: 72,
    textAlign: "right",
  },
  moneyColumn: {
    width: 104,
    textAlign: "right",
  },
  items: {
    minHeight: 0,
    flex: 1,
  },
  empty: {
    marginTop: 22,
    color: "rgba(255,255,255,0.5)",
    fontSize: 18,
    fontWeight: "500",
  },
  itemRow: {
    height: 32,
    flexGrow: 0,
    flexShrink: 0,
    flexDirection: "row",
    alignItems: "center",
    borderBottomWidth: 1,
    borderBottomColor: "rgba(255,255,255,0.15)",
  },
  itemText: {
    color: "#ffffff",
    fontSize: 16,
    fontWeight: "500",
    fontVariant: ["tabular-nums"],
  },
  itemTextStrong: {
    color: "#ffffff",
    fontSize: 17,
    fontWeight: "700",
    fontVariant: ["tabular-nums"],
  },
  secondaryText: {
    color: "rgba(255,255,255,0.66)",
    fontSize: 16,
    fontVariant: ["tabular-nums"],
  },
  summaryPanel: {
    height: 132,
    flexDirection: "row",
    padding: 16,
    backgroundColor: "#071426",
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.24)",
    borderRadius: 12,
  },
  summaryMetrics: {
    flex: 47,
    justifyContent: "space-between",
    paddingRight: 18,
  },
  summaryCounts: {
    color: "rgba(255,255,255,0.72)",
    fontSize: 17,
    fontWeight: "600",
  },
  metricRow: {
    flexDirection: "row",
    alignItems: "stretch",
  },
  metric: {
    flex: 1,
    justifyContent: "flex-end",
  },
  metricSeparator: {
    width: 1,
    marginHorizontal: 14,
    backgroundColor: "rgba(255,255,255,0.26)",
  },
  metricLabel: {
    marginBottom: 5,
    color: "rgba(255,255,255,0.64)",
    fontSize: 14,
    fontWeight: "500",
  },
  metricValue: {
    color: "#ffffff",
    fontSize: 23,
    fontWeight: "700",
    fontVariant: ["tabular-nums"],
  },
  amountDue: {
    flex: 26,
    justifyContent: "center",
    paddingHorizontal: 18,
    borderLeftWidth: 1,
    borderRightWidth: 1,
    borderColor: "rgba(255,255,255,0.26)",
  },
  amountDueLabel: {
    marginBottom: 3,
    color: "rgba(255,255,255,0.82)",
    fontSize: 17,
    fontWeight: "600",
  },
  amountDueValue: {
    color: "#ffc21c",
    fontSize: 42,
    fontWeight: "800",
    fontVariant: ["tabular-nums"],
  },
  statusRegion: {
    flex: 27,
    paddingLeft: 16,
  },
  statusCard: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 14,
    borderWidth: 1,
    borderColor: "#72e6c3",
    borderRadius: 10,
  },
  statusTitle: {
    color: "#72e6c3",
    fontSize: 27,
    fontWeight: "700",
    textAlign: "center",
  },
  statusSubtitle: {
    marginTop: 7,
    color: "rgba(255,255,255,0.88)",
    fontSize: 15,
    fontWeight: "500",
    textAlign: "center",
  },
});
