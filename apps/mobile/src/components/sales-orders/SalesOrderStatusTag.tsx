import { StyleSheet, View } from "react-native";
import { Text } from "react-native-paper";
import { resolveSalesOrderStatusKey } from "@/modules/sales-orders/logic";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS } from "@/shared/theme/tokens";

const TONES: Record<string, { background: string; color: string }> = {
  pending: { background: "#FAEEDA", color: "#854F0B" },
  paid: { background: "#E1F5EE", color: HB_COLORS.success },
  cancelled: { background: HB_COLORS.surfaceMuted, color: HB_COLORS.textSecondary },
  refunded: { background: "#FCEBEB", color: HB_COLORS.danger },
  installment: { background: "#E6F1FB", color: HB_COLORS.action },
  unknown: { background: HB_COLORS.surfaceMuted, color: HB_COLORS.textSecondary },
};

export function SalesOrderStatusTag({ status }: { status: number | null }) {
  const { t } = useAppTranslation("salesOrders");
  const key = resolveSalesOrderStatusKey(status);
  const tone = TONES[key] ?? TONES.unknown;
  return (
    <View style={[styles.tag, { backgroundColor: tone.background }]}>
      <Text style={[styles.text, { color: tone.color }]}>{t(`status.${key}`)}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  tag: { paddingHorizontal: 8, paddingVertical: 2, borderRadius: 6 },
  text: { fontSize: 11, fontWeight: "700" },
});
