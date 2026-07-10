import { useState } from "react";
import { StyleSheet, View } from "react-native";
import { SegmentedButtons, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { ProductReportScreen } from "@/modules/product-report/product-report-screen";
import { RevenueReportScreen } from "@/modules/reports/RevenueReportScreen";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

type ReportTab = "revenue" | "product";

export function ReportsHubScreen() {
  const { t } = useAppTranslation("common");
  const [tab, setTab] = useState<ReportTab>("revenue");

  return (
    <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
      <View style={styles.header}>
        <Text variant="headlineSmall" style={styles.title}>
          {t("reports.title")}
        </Text>
        <SegmentedButtons
          value={tab}
          onValueChange={(value) => setTab(value as ReportTab)}
          buttons={[
            { value: "revenue", label: t("reports.sections.revenue") },
            { value: "product", label: t("reports.sections.product") },
          ]}
        />
      </View>

      {tab === "revenue" ? <RevenueReportScreen embedded /> : <ProductReportScreen embedded />}
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#F7F8FA",
  },
  header: {
    gap: 12,
    padding: 16,
    paddingBottom: 8,
  },
  title: {
    color: "#111827",
    fontWeight: "700",
  },
});
