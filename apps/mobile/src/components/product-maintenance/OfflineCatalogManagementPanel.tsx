/**
 * 设置页「离线商品数据」管理面板（仅设备注册绑定会话渲染）。
 *
 * - 状态、进度、错误全部订阅 offline-catalog-store 里的刷新协调器，与商品查询页的状态行同源；
 * - 「切换分店」复用 useStores.selectStore，保证工作台、商品查询、设置三处的当前分店一致；
 * - 分店列表内联渲染而不是用 StorePickerModal：设置页的详情弹窗是原生 Modal，
 *   Paper 的 Portal 会被压在它下面，弹出的选择器根本看不见。
 */
import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { Pressable, StyleSheet, View } from "react-native";
import { Button, ProgressBar, RadioButton, Text } from "react-native-paper";
import type { OfflineCatalogRefreshState } from "@/modules/product-maintenance/offline-catalog/offline-catalog-refresh-coordinator";
import { useOfflineCatalogStore } from "@/modules/product-maintenance/offline-catalog/offline-catalog-store";
import type { ActiveOfflineCatalogMetadata } from "@/modules/product-maintenance/offline-catalog/types";
import type { Store } from "@/modules/shop/types";
import { useStores } from "@/modules/shop/use-stores";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { formatOfflineCatalogCount, formatOfflineCatalogTime } from "./OfflineModeBanner";

type Translate = (key: string, options?: Record<string, unknown>) => string;

export type OfflineCatalogSummaryTone = "success" | "warning" | "danger" | "neutral";

export interface OfflineCatalogSummary {
  /** settings 命名空间下 offlineData.status* 的键名。 */
  statusKey: "statusReady" | "statusSyncing" | "statusFailed" | "statusMissing";
  tone: OfflineCatalogSummaryTone;
  /** 一行摘要：更新时间与件数 / 进度 / 失败原因 / 未下载。 */
  summary: string;
}

/** 设置首页的行与面板共用同一份摘要文案，避免两处状态口径不一致。 */
export function describeOfflineCatalogSummary(input: {
  refresh: OfflineCatalogRefreshState;
  storeCode: string | null;
  activeMeta: ActiveOfflineCatalogMetadata | null;
  language: string;
  t: Translate;
}): OfflineCatalogSummary {
  const { refresh, storeCode, activeMeta, language, t } = input;
  if (refresh.kind === "running" && refresh.storeCode === storeCode) {
    return {
      statusKey: "statusSyncing",
      tone: "warning",
      summary: t("offlineData.summarySyncing", { percent: refresh.progress.percent }),
    };
  }
  if (refresh.kind === "failed" && refresh.storeCode === storeCode) {
    return {
      statusKey: "statusFailed",
      tone: "danger",
      summary: t("offlineData.summaryFailed", {
        reason: t(`productQuery:offline.errors.${refresh.errorCode}`),
      }),
    };
  }
  if (activeMeta) {
    return {
      statusKey: "statusReady",
      tone: "success",
      summary: t("offlineData.summaryReady", {
        time: formatOfflineCatalogTime(activeMeta.generatedAt, language),
        count: formatOfflineCatalogCount(activeMeta.itemCount, language),
      }),
    };
  }
  return { statusKey: "statusMissing", tone: "neutral", summary: t("offlineData.summaryMissing") };
}

interface InfoRowProps {
  label: string;
  value: string;
  action?: ReactNode;
}

function InfoRow({ label, value, action }: InfoRowProps) {
  return (
    <View style={styles.infoRow}>
      <View style={styles.infoText}>
        <Text variant="bodySmall" style={styles.meta}>
          {label}
        </Text>
        <Text variant="bodyMedium" style={styles.value} numberOfLines={2}>
          {value}
        </Text>
      </View>
      {action ? <View style={styles.infoAction}>{action}</View> : null}
    </View>
  );
}

interface OfflineCatalogManagementPanelProps {
  /** 刷新完成后的轻提示；设置页用 Alert 或 Snackbar 都可以，由调用方决定。 */
  onNotify?: (message: string) => void;
}

export function OfflineCatalogManagementPanel({ onNotify }: OfflineCatalogManagementPanelProps) {
  const { t, language } = useAppTranslation(["settings", "productQuery", "common"]);
  const { stores, selectedStore, selectedStoreCode, selectStore, isDeviceMode } = useStores();
  const activeMetaMap = useOfflineCatalogStore((state) => state.activeMeta);
  const refresh = useOfflineCatalogStore((state) => state.refresh);
  const dbError = useOfflineCatalogStore((state) => state.dbError);
  const [storeListVisible, setStoreListVisible] = useState(false);
  const [switching, setSwitching] = useState(false);

  const activeMeta = selectedStoreCode ? (activeMetaMap[selectedStoreCode] ?? null) : null;
  const running = refresh.kind === "running" && refresh.storeCode === selectedStoreCode;
  const canSwitchStore = !isDeviceMode && stores.length > 1;
  const summary = describeOfflineCatalogSummary({
    refresh,
    storeCode: selectedStoreCode,
    activeMeta,
    language,
    t,
  });
  const storeCodesKey = useMemo(() => stores.map((store) => store.storeCode).join("|"), [stores]);

  useEffect(() => {
    // 打开面板就把所有可选分店的快照摘要读出来，切店列表里能直接看到哪些店已经下载过。
    let cancelled = false;
    void (async () => {
      const store = useOfflineCatalogStore.getState();
      if (!(await store.open()) || cancelled) {
        return;
      }
      for (const storeCode of storeCodesKey.split("|").filter(Boolean)) {
        if (cancelled) {
          return;
        }
        await store.loadActiveMeta(storeCode);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [storeCodesKey]);

  const handleRefresh = useCallback(() => {
    if (!selectedStoreCode) {
      return;
    }
    void useOfflineCatalogStore
      .getState()
      .refreshCatalog(selectedStoreCode)
      .then((result) => {
        if (!result) {
          return;
        }
        onNotify?.(
          result.mode === "noChange"
            ? t("offlineData.refreshNoChange")
            : t("offlineData.refreshCompleted", {
                count: formatOfflineCatalogCount(result.metadata.itemCount, language),
              }),
        );
      });
  }, [language, onNotify, selectedStoreCode, t]);

  const handleCancel = useCallback(() => {
    useOfflineCatalogStore.getState().cancelRefresh();
  }, []);

  const handleSelectStore = useCallback(
    async (store: Store) => {
      setStoreListVisible(false);
      if (store.storeCode === selectedStoreCode) {
        return;
      }
      setSwitching(true);
      try {
        // 与工作台同一入口切换全局当前分店；快照按店隔离，切换后立即读取该店摘要。
        await selectStore(store);
        await useOfflineCatalogStore.getState().loadActiveMeta(store.storeCode);
      } finally {
        setSwitching(false);
      }
    },
    [selectStore, selectedStoreCode],
  );

  const storeLabel = selectedStore
    ? selectedStore.storeName && selectedStore.storeName !== selectedStore.storeCode
      ? `${selectedStore.storeName} · ${selectedStore.storeCode}`
      : selectedStore.storeCode
    : t("offlineData.storeNotSelected");

  return (
    <View style={styles.root}>
      <View style={styles.block}>
        <InfoRow
          label={t("offlineData.currentStore")}
          value={storeLabel}
          action={
            canSwitchStore ? (
              <Button
                compact
                mode="text"
                icon={storeListVisible ? "chevron-up" : "swap-horizontal"}
                onPress={() => setStoreListVisible((visible) => !visible)}
                loading={switching}
                disabled={running || switching}
              >
                {t("offlineData.switchStore")}
              </Button>
            ) : undefined
          }
        />
        {isDeviceMode ? (
          <Text variant="bodySmall" style={[styles.meta, styles.hint]}>
            {t("offlineData.deviceBoundStoreHint")}
          </Text>
        ) : null}
        {storeListVisible && canSwitchStore ? (
          <View style={styles.storeList} testID="settings-offline-data-store-list">
            {stores.map((store) => {
              const selected = store.storeCode === selectedStoreCode;
              const storeMeta = activeMetaMap[store.storeCode] ?? null;
              const metaText = storeMeta
                ? t("offlineData.summaryReady", {
                    time: formatOfflineCatalogTime(storeMeta.generatedAt, language),
                    count: formatOfflineCatalogCount(storeMeta.itemCount, language),
                  })
                : t("offlineData.summaryMissing");
              return (
                <Pressable
                  key={store.storeCode}
                  accessibilityRole="button"
                  accessibilityState={{ selected }}
                  accessibilityLabel={`${store.storeName || store.storeCode}. ${metaText}`}
                  onPress={() => void handleSelectStore(store)}
                  style={({ pressed }) => [styles.storeRow, pressed && styles.storeRowPressed]}
                >
                  <RadioButton
                    value={store.storeCode}
                    status={selected ? "checked" : "unchecked"}
                    onPress={() => void handleSelectStore(store)}
                  />
                  <View style={styles.infoText}>
                    <Text variant="bodyMedium" style={styles.value} numberOfLines={1}>
                      {store.storeName || store.storeCode}
                    </Text>
                    <Text variant="bodySmall" style={styles.meta} numberOfLines={1}>
                      {metaText}
                    </Text>
                  </View>
                </Pressable>
              );
            })}
          </View>
        ) : null}
        <View style={styles.divider} />
        <InfoRow label={t("offlineData.statusLabel")} value={summary.summary} />
        {activeMeta ? (
          <>
            <View style={styles.divider} />
            <InfoRow
              label={t("offlineData.updatedAt")}
              value={formatOfflineCatalogTime(activeMeta.generatedAt, language)}
            />
            <View style={styles.divider} />
            <InfoRow
              label={t("offlineData.downloadedAt")}
              value={formatOfflineCatalogTime(activeMeta.activatedAt, language)}
            />
            <View style={styles.divider} />
            <InfoRow
              label={t("offlineData.itemCount")}
              value={t("offlineData.itemCountValue", {
                count: formatOfflineCatalogCount(activeMeta.itemCount, language),
              })}
            />
          </>
        ) : null}
      </View>

      {refresh.kind === "running" && refresh.storeCode === selectedStoreCode ? (
        <View style={styles.progressBox}>
          <ProgressBar progress={refresh.progress.percent / 100} color={HB_COLORS.action} />
          <Text variant="bodySmall" style={styles.meta}>
            {t("offlineData.progress", {
              step: t(`productQuery:offline.steps.${refresh.progress.step}`),
              percent: refresh.progress.percent,
              completed: formatOfflineCatalogCount(refresh.progress.completedItemCount, language),
              total:
                refresh.progress.totalItemCount === null
                  ? "--"
                  : formatOfflineCatalogCount(refresh.progress.totalItemCount, language),
            })}
          </Text>
        </View>
      ) : null}

      {dbError ? (
        <Text variant="bodySmall" style={styles.errorText}>
          {dbError}
        </Text>
      ) : null}

      <View style={styles.actions}>
        {running ? (
          <Button mode="outlined" icon="close" onPress={handleCancel} style={styles.actionButton}>
            {t("offlineData.cancelUpdate")}
          </Button>
        ) : (
          <Button
            mode="contained"
            icon="cloud-download-outline"
            onPress={handleRefresh}
            disabled={!selectedStoreCode || switching}
            style={styles.actionButton}
          >
            {activeMeta ? t("offlineData.updateNow") : t("offlineData.downloadNow")}
          </Button>
        )}
      </View>

      <Text variant="bodySmall" style={styles.meta}>
        {t("offlineData.manualRefreshHint")}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  root: {
    gap: HB_SPACING.md,
  },
  block: {
    overflow: "hidden",
    borderRadius: HB_RADIUS.surface,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: HB_COLORS.outlineMuted,
    backgroundColor: HB_COLORS.surfaceMuted,
  },
  infoRow: {
    minHeight: 56,
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.sm,
    paddingHorizontal: HB_SPACING.md,
    paddingVertical: HB_SPACING.xs,
  },
  infoText: {
    flex: 1,
    minWidth: 0,
    gap: 2,
  },
  infoAction: {
    flexShrink: 0,
  },
  hint: {
    paddingHorizontal: HB_SPACING.md,
    paddingBottom: HB_SPACING.xs,
  },
  storeList: {
    marginHorizontal: HB_SPACING.sm,
    marginBottom: HB_SPACING.sm,
    borderRadius: HB_RADIUS.control,
    backgroundColor: HB_COLORS.surface,
    overflow: "hidden",
  },
  storeRow: {
    minHeight: 52,
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xxs,
    paddingRight: HB_SPACING.sm,
  },
  storeRowPressed: {
    backgroundColor: HB_COLORS.surfaceMuted,
  },
  divider: {
    height: StyleSheet.hairlineWidth,
    backgroundColor: HB_COLORS.outlineMuted,
  },
  value: {
    color: HB_COLORS.textPrimary,
    fontWeight: "600",
  },
  meta: {
    color: HB_COLORS.textSecondary,
  },
  progressBox: {
    gap: HB_SPACING.xs,
  },
  errorText: {
    color: HB_COLORS.danger,
  },
  actions: {
    flexDirection: "row",
    gap: HB_SPACING.xs,
  },
  actionButton: {
    flex: 1,
  },
});
