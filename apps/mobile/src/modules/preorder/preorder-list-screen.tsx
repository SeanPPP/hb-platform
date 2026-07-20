import { useCallback } from "react";
import { FlatList, StyleSheet, View } from "react-native";
import { type Href, useRouter } from "expo-router";
import { Button, Card, Chip, IconButton, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { LoadingOverlay } from "@/components/ui/LoadingOverlay";
import { useStores } from "@/modules/shop/use-stores";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { formatBrisbaneBusinessDate } from "./business-date";
import type { PreorderActivationSummary } from "./types";
import { usePreorderGate } from "./use-preorder-gate";

function formatDeadline(value: string, language: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value || "--";
  return new Intl.DateTimeFormat(language === "en" ? "en-AU" : "zh-CN", {
    dateStyle: "medium",
    timeStyle: "short",
    timeZone: "Australia/Brisbane",
  }).format(date);
}

export function PreorderListScreen() {
  const router = useRouter();
  const { t, language } = useAppTranslation(["preorder", "common"]);
  const { selectedStore, selectedStoreCode } = useStores();
  const gate = usePreorderGate(selectedStoreCode);

  const openActivation = useCallback((activation: PreorderActivationSummary) => {
    if (!selectedStoreCode) return;
    router.push({
      pathname: "/preorders/[activationGuid]",
      params: { activationGuid: activation.activationGuid, storeCode: selectedStoreCode },
    } as unknown as Href);
  }, [router, selectedStoreCode]);

  return (
    <SafeAreaView edges={["top", "bottom", "left", "right"]} style={styles.container}>
      <View style={styles.header}>
        <IconButton
          icon="arrow-left"
          accessibilityLabel={t("common:actions.back")}
          onPress={() => router.back()}
          style={styles.headerButton}
        />
        <View style={styles.headerText}>
          <Text variant="titleLarge" style={styles.title}>{t("list.title")}</Text>
          <Text variant="bodySmall" style={styles.subtitle}>{t("list.subtitle")}</Text>
        </View>
      </View>

      <View style={styles.storeRow}>
        <Chip icon="storefront-outline" compact>{selectedStore?.storeName || t("common:labels.selectStore")}</Chip>
      </View>

      <FlatList
        data={gate.activations}
        keyExtractor={(item) => item.activationGuid}
        contentContainerStyle={styles.listContent}
        renderItem={({ item }) => {
          const estimatedArrivalDate = formatBrisbaneBusinessDate(item.estimatedArrivalDate);
          return (
            <Card mode="outlined" style={styles.card}>
              <Card.Content style={styles.cardContent}>
                <View style={styles.cardHeading}>
                  <View style={styles.cardTitleWrap}>
                    <Text variant="titleMedium" style={styles.cardTitle}>{item.templateName}</Text>
                    <Text variant="bodySmall" style={styles.code}>{item.activationCode}</Text>
                  </View>
                  <Chip compact icon="calendar-clock">{t("list.period", { period: item.periodNumber })}</Chip>
                </View>
                <View style={styles.scheduleRow}>
                  <Text variant="bodyMedium" style={styles.deadline}>
                    {t("list.deadline", { value: formatDeadline(item.endAtUtc, language) })}
                  </Text>
                  {estimatedArrivalDate ? (
                    <Text variant="bodySmall" style={styles.estimatedArrival}>
                      {t("list.estimatedArrival", { value: estimatedArrivalDate })}
                    </Text>
                  ) : null}
                </View>
                <Button
                  mode="contained"
                  icon="arrow-right"
                  contentStyle={styles.openButtonContent}
                  onPress={() => openActivation(item)}
                >
                  {t("list.open")}
                </Button>
              </Card.Content>
            </Card>
          );
        }}
        ListEmptyComponent={
          gate.isError ? (
            <EmptyState
              title={t("list.loadFailedTitle")}
              description={t("list.loadFailedDescription")}
              actionLabel={t("common:actions.retry")}
              onAction={() => void gate.refresh()}
            />
          ) : !gate.isChecking ? (
            <EmptyState
              title={selectedStoreCode ? t("list.completedTitle") : t("list.selectStoreTitle")}
              description={selectedStoreCode ? t("list.completedDescription") : t("list.selectStoreDescription")}
              actionLabel={t("list.backToShop")}
              onAction={() => router.replace("/(tabs)/home")}
            />
          ) : null
        }
      />
      {gate.isChecking ? <LoadingOverlay /> : null}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#F6FAFE" },
  header: {
    minHeight: 64,
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: 8,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#D9E0E8",
    backgroundColor: "#FFFFFF",
  },
  headerButton: { width: 44, height: 44, margin: 0 },
  headerText: { flex: 1, paddingRight: 12 },
  title: { color: "#0F172A", fontWeight: "700" },
  subtitle: { color: "#5B6474" },
  storeRow: { paddingHorizontal: 12, paddingTop: 10 },
  listContent: { flexGrow: 1, padding: 12, paddingBottom: 24, gap: 10 },
  card: { backgroundColor: "#FFFFFF", borderColor: "#D9E0E8" },
  cardContent: { gap: 12 },
  cardHeading: { flexDirection: "row", alignItems: "flex-start", gap: 10 },
  cardTitleWrap: { flex: 1, gap: 2 },
  cardTitle: { color: "#172033", fontWeight: "700" },
  code: { color: "#667085" },
  scheduleRow: { gap: 3 },
  deadline: { color: "#8A4B08", fontWeight: "600" },
  estimatedArrival: { color: "#667085", fontWeight: "600" },
  openButtonContent: { minHeight: 44 },
});
