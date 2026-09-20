import { StyleSheet, View } from "react-native";
import { ActivityIndicator, Button, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { OfflineCatalogRefreshState } from "@/modules/product-maintenance/offline-catalog/offline-catalog-refresh-coordinator";
import type { ActiveOfflineCatalogMetadata } from "@/modules/product-maintenance/offline-catalog/types";
import { formatOfflineCatalogCount, formatOfflineCatalogTime } from "./OfflineModeBanner";

interface OfflineCatalogStatusRowProps {
  storeCode: string | null;
  activeMeta: ActiveOfflineCatalogMetadata | null;
  refresh: OfflineCatalogRefreshState;
  online: boolean;
  onRefresh: () => void;
  onCancel: () => void;
}

/**
 * 离线数据状态行：一行灰字告诉员工本地快照的更新时间与件数，
 * 同步中显示步骤与进度，失败显示原因并可重试。只在离线资格会话下渲染。
 */
export function OfflineCatalogStatusRow({
  storeCode,
  activeMeta,
  refresh,
  online,
  onRefresh,
  onCancel,
}: OfflineCatalogStatusRowProps) {
  const { t, language } = useAppTranslation(["productQuery", "common"]);
  const running = refresh.kind === "running" && refresh.storeCode === storeCode;
  const failed = refresh.kind === "failed" && refresh.storeCode === storeCode;

  let label: string;
  if (running) {
    const { progress } = refresh;
    label = t("offline.statusSyncing", {
      step: t(`offline.steps.${progress.step}`),
      percent: progress.percent,
      completed: formatOfflineCatalogCount(progress.completedItemCount, language),
      total: progress.totalItemCount === null ? "--" : formatOfflineCatalogCount(progress.totalItemCount, language),
    });
  } else if (failed) {
    label = t("offline.statusFailed", { reason: t(`offline.errors.${refresh.errorCode}`) });
  } else if (activeMeta) {
    label = t("offline.statusReady", {
      time: formatOfflineCatalogTime(activeMeta.generatedAt, language),
      count: formatOfflineCatalogCount(activeMeta.itemCount, language),
    });
  } else {
    label = t("offline.statusMissing");
  }

  return (
    <View style={styles.container}>
      {running ? <ActivityIndicator size={12} color="#2563EB" /> : null}
      <Text variant="labelSmall" style={[styles.label, failed ? styles.labelFailed : null]} numberOfLines={1}>
        {label}
      </Text>
      {running ? (
        <Button compact mode="text" onPress={onCancel} labelStyle={styles.actionLabel}>
          {t("common:actions.cancel")}
        </Button>
      ) : (
        <Button
          compact
          mode="text"
          onPress={onRefresh}
          disabled={!online || !storeCode}
          labelStyle={styles.actionLabel}
        >
          {failed ? t("offline.retry") : activeMeta ? t("offline.update") : t("offline.download")}
        </Button>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    minHeight: 28,
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    paddingHorizontal: 16,
    marginTop: -4,
    marginBottom: 2,
  },
  label: {
    flex: 1,
    minWidth: 0,
    color: "#98A2B3",
  },
  labelFailed: {
    color: "#B42318",
  },
  actionLabel: {
    fontSize: 12,
    marginVertical: 2,
  },
});
