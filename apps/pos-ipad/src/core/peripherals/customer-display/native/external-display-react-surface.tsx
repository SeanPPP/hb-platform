import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AppRegistry,
  StyleSheet,
  Text,
  View,
} from "react-native";

import {
  CustomerDisplaySnapshotSchema,
  type CustomerDisplaySnapshot,
} from "../../../contracts/external-display";

import type { ExternalDisplayNativeModule } from "./external-display-bridge";

const surfaceModuleName = "HBExternalDisplay";
const maximumVisibleItems = 12;

type ExternalDisplaySurfaceProps = {
  surfaceId: string;
  snapshot: CustomerDisplaySnapshot | null;
};

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

function modeTitle(
  mode: CustomerDisplaySnapshot["mode"],
  translate: (key: string) => string,
): string {
  switch (mode) {
    case "idle":
      return translate("customerDisplay.mode.idle");
    case "cart":
      return translate("customerDisplay.mode.cart");
    case "payment":
      return translate("customerDisplay.mode.payment");
    case "change":
      return translate("customerDisplay.mode.change");
    case "success":
      return translate("customerDisplay.mode.success");
  }
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

  const visibleItems = useMemo(
    () => snapshot?.items.slice(0, maximumVisibleItems) ?? [],
    [snapshot],
  );
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
      style={[styles.surface, styles.transactionLayout]}
      testID="external-display-surface"
    >
      <View
        style={styles.transactionPanel}
        testID="external-display-transaction-panel"
      >
        <Text style={styles.mode}>
          {snapshot === null
            ? t("customerDisplay.mode.idle")
            : modeTitle(snapshot.mode, t)}
        </Text>

        <View style={styles.items}>
          {visibleItems.length === 0 ? (
            <Text style={styles.empty}>{t("customerDisplay.empty")}</Text>
          ) : (
            visibleItems.map((item, index) => (
              <View
                key={`${index}-${item.name}`}
                style={styles.itemRow}
              >
                <Text numberOfLines={1} style={styles.itemName}>
                  {item.name}
                </Text>
                <Text style={styles.quantity}>× {item.quantity}</Text>
                <Text style={styles.amount}>{formatMoney(item.amount)}</Text>
              </View>
            ))
          )}
        </View>

        <Text style={styles.itemCount}>
          {snapshot === null
            ? t("customerDisplay.ready")
            : `${t("customerDisplay.items", {
                count: snapshot.items.length,
              })}${
                snapshot.items.length > maximumVisibleItems
                  ? ` · ${t("customerDisplay.more", {
                      count: snapshot.items.length - maximumVisibleItems,
                    })}`
                  : ""
              }`}
        </Text>

        <View style={styles.divider} />
        <AmountRow
          label={t("customerDisplay.gst")}
          value={snapshot === null ? "$0.00" : formatMoney(snapshot.gst)}
        />
        <AmountRow
          label={t("customerDisplay.discount")}
          value={
            snapshot === null ? "$0.00" : formatDiscount(snapshot.discount)
          }
        />
        <AmountRow
          emphasized
          label={t("customerDisplay.total")}
          value={snapshot === null ? "$0.00" : formatMoney(snapshot.total)}
        />
        <AmountRow
          emphasized
          label={t("customerDisplay.change")}
          value={snapshot === null ? "$0.00" : formatMoney(snapshot.change)}
        />
      </View>

      {/* 右侧保持透明，由 UIKit 本地媒体层承载 image/video 广告。 */}
      <View
        style={styles.nativeAdvertWindow}
        testID="external-display-advert-window"
      />
    </View>
  );
}

function AmountRow({
  emphasized = false,
  label,
  value,
}: {
  emphasized?: boolean;
  label: string;
  value: string;
}) {
  return (
    <View style={styles.totalRow}>
      <Text style={emphasized ? styles.totalLabelStrong : styles.totalLabel}>
        {label}
      </Text>
      <Text style={emphasized ? styles.totalValueStrong : styles.totalValue}>
        {value}
      </Text>
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
    flexDirection: "row",
    backgroundColor: "transparent",
  },
  transactionLayout: {
    gap: 32,
    paddingHorizontal: 34,
    paddingVertical: 28,
  },
  transactionPanel: {
    flex: 3,
    backgroundColor: "#09111f",
  },
  nativeAdvertWindow: {
    flex: 2,
    backgroundColor: "transparent",
  },
  fullScreenNativeAdvertWindow: {
    flex: 1,
    backgroundColor: "transparent",
  },
  mode: {
    marginBottom: 18,
    color: "#69e3c2",
    fontSize: 34,
    fontWeight: "600",
  },
  items: {
    flex: 1,
    gap: 3,
  },
  empty: {
    color: "rgba(255,255,255,0.5)",
    fontSize: 23,
    fontWeight: "500",
  },
  itemRow: {
    minHeight: 34,
    flexDirection: "row",
    alignItems: "center",
    gap: 14,
  },
  itemName: {
    flex: 1,
    color: "#ffffff",
    fontSize: 21,
    fontWeight: "500",
  },
  quantity: {
    minWidth: 76,
    color: "rgba(255,255,255,0.64)",
    fontSize: 19,
    fontVariant: ["tabular-nums"],
    textAlign: "right",
  },
  amount: {
    minWidth: 116,
    color: "#ffffff",
    fontSize: 22,
    fontWeight: "600",
    fontVariant: ["tabular-nums"],
    textAlign: "right",
  },
  itemCount: {
    marginTop: 12,
    color: "rgba(255,255,255,0.58)",
    fontSize: 17,
    fontWeight: "500",
  },
  divider: {
    height: StyleSheet.hairlineWidth,
    marginVertical: 15,
    backgroundColor: "rgba(255,255,255,0.12)",
  },
  totalRow: {
    minHeight: 42,
    flexDirection: "row",
    alignItems: "baseline",
    justifyContent: "space-between",
  },
  totalLabel: {
    color: "rgba(255,255,255,0.66)",
    fontSize: 19,
    fontWeight: "500",
  },
  totalLabelStrong: {
    color: "#ffffff",
    fontSize: 25,
    fontWeight: "700",
  },
  totalValue: {
    color: "#ffffff",
    fontSize: 22,
    fontWeight: "500",
    fontVariant: ["tabular-nums"],
  },
  totalValueStrong: {
    color: "#ffc73d",
    fontSize: 38,
    fontWeight: "700",
    fontVariant: ["tabular-nums"],
  },
});
