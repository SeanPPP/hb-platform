import { StyleSheet, View } from "react-native";
import { Button, Card, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_SPACING } from "@/shared/theme/tokens";

interface SupplyRestockedBannerProps {
  restockedCount: number;
  onOpen: () => void;
}

/** 订货首页横幅：关注的商品已恢复订货。样式对齐 Preorder 门禁横幅，但用成功色。 */
export function SupplyRestockedBanner({ restockedCount, onOpen }: SupplyRestockedBannerProps) {
  const { t } = useAppTranslation("supplyNotice");
  if (restockedCount <= 0) {
    return null;
  }
  return (
    <Card mode="contained" style={styles.card} testID="supply-restocked-banner">
      <Card.Content style={styles.content}>
        <View style={styles.textWrap}>
          <Text variant="titleSmall" style={styles.title}>{t("restockedBanner", { count: restockedCount })}</Text>
        </View>
        <Button compact mode="contained" icon="bell-ring-outline" onPress={onOpen} contentStyle={styles.buttonContent}>
          {t("restockedBannerAction")}
        </Button>
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: { marginHorizontal: HB_SPACING.sm, marginVertical: HB_SPACING.xs, backgroundColor: "#E6F7EC", borderColor: "#7BC89A", borderWidth: 1 },
  content: { minHeight: 56, flexDirection: "row", alignItems: "center", gap: 10, paddingVertical: 8 },
  textWrap: { flex: 1 },
  title: { color: "#0B5C2A", fontWeight: "700" },
  buttonContent: { minHeight: 40 },
});
