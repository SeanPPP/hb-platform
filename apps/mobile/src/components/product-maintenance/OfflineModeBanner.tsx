import { StyleSheet, View } from "react-native";
import { Button, Icon, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { ActiveOfflineCatalogMetadata } from "@/modules/product-maintenance/offline-catalog/types";

interface OfflineModeBannerProps {
  activeMeta: ActiveOfflineCatalogMetadata | null;
  retrying?: boolean;
  onRetry: () => void;
}

/** 按本地时区格式化到分钟：`09-17 10:20`。 */
export function formatOfflineCatalogTime(iso: string | null | undefined, language: string): string {
  if (!iso) {
    return "--";
  }
  const date = new Date(iso);
  if (Number.isNaN(date.valueOf())) {
    return "--";
  }
  return date.toLocaleString(language === "zh" ? "zh-CN" : "en-AU", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  });
}

export function formatOfflineCatalogCount(count: number, language: string): string {
  return count.toLocaleString(language === "zh" ? "zh-CN" : "en-AU");
}

/**
 * 离线横幅：必须清楚告诉门店员工当前看到的是哪一刻的数据。
 * 主行显示服务端数据生成时间，副行显示件数与本机下载时间。
 */
export function OfflineModeBanner({ activeMeta, retrying = false, onRetry }: OfflineModeBannerProps) {
  const { t, language } = useAppTranslation(["productQuery", "common"]);

  return (
    <View accessibilityRole="alert" accessibilityLiveRegion="polite" style={styles.container}>
      <Icon source="cloud-off-outline" size={18} color="#B45309" />
      <View style={styles.copy}>
        <Text variant="labelMedium" style={styles.title} numberOfLines={1}>
          {activeMeta
            ? t("offline.bannerTitle", { time: formatOfflineCatalogTime(activeMeta.generatedAt, language) })
            : t("offline.bannerNoData")}
        </Text>
        {activeMeta ? (
          <Text variant="bodySmall" style={styles.meta} numberOfLines={1}>
            {t("offline.bannerMeta", {
              count: formatOfflineCatalogCount(activeMeta.itemCount, language),
              time: formatOfflineCatalogTime(activeMeta.activatedAt, language),
            })}
          </Text>
        ) : (
          <Text variant="bodySmall" style={styles.meta} numberOfLines={1}>
            {t("offline.bannerNoDataHint")}
          </Text>
        )}
      </View>
      <Button compact mode="text" onPress={onRetry} loading={retrying} disabled={retrying} textColor="#B45309">
        {t("offline.retryConnection")}
      </Button>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    minHeight: 48,
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    marginHorizontal: 16,
    marginBottom: 6,
    borderRadius: 10,
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: "#FDE68A",
    backgroundColor: "#FFFBEB",
    paddingHorizontal: 12,
    paddingVertical: 6,
  },
  copy: {
    flex: 1,
    minWidth: 0,
    gap: 1,
  },
  title: {
    color: "#92400E",
    fontWeight: "700",
  },
  meta: {
    color: "#B45309",
  },
});
