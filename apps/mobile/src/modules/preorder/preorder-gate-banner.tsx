import { StyleSheet, View } from "react-native";
import { Button, Card, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import type { PreorderGateState } from "./use-preorder-gate";

interface PreorderGateBannerProps {
  gate: PreorderGateState;
  onOpen: () => void;
}

export function PreorderGateBanner({ gate, onOpen }: PreorderGateBannerProps) {
  const { t } = useAppTranslation(["preorder"]);
  if (!gate.normalOrderBlocked) {
    return null;
  }

  const description = gate.isError
    ? t("gate.unavailable")
    : gate.isChecking
      ? t("gate.checking")
      : t("gate.pending", { count: gate.activations.length });

  return (
    <Card mode="contained" style={styles.card}>
      <Card.Content style={styles.content}>
        <View style={styles.textWrap}>
          <Text variant="titleSmall" style={styles.title}>{t("gate.title")}</Text>
          <Text variant="bodySmall" style={styles.description}>{description}</Text>
        </View>
        <Button
          compact
          mode="contained"
          icon="clipboard-list-outline"
          onPress={onOpen}
          contentStyle={styles.buttonContent}
        >
          {t("gate.action")}
        </Button>
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 12,
    marginVertical: 8,
    backgroundColor: "#FFF7E6",
    borderColor: "#F5B041",
    borderWidth: 1,
  },
  content: {
    minHeight: 64,
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    paddingVertical: 10,
  },
  textWrap: {
    flex: 1,
    gap: 2,
  },
  title: {
    color: "#7A3E00",
    fontWeight: "700",
  },
  description: {
    color: "#6B4B24",
    lineHeight: 18,
  },
  buttonContent: {
    minHeight: 44,
  },
});
