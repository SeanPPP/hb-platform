import { StyleSheet, View } from "react-native";
import { Button, Card } from "react-native-paper";

interface LabelPrintCardProps {
  isPrintingProduct?: boolean;
  isPrintingDiscount?: boolean;
  isPrintingBigDiscount?: boolean;
  canPrintDiscount?: boolean;
  canPrintBigDiscount?: boolean;
  onPrintProduct?: () => void;
  onPrintDiscount?: () => void;
  onPrintBigDiscount?: () => void;
}

export function LabelPrintCard({
  isPrintingProduct = false,
  isPrintingDiscount = false,
  isPrintingBigDiscount = false,
  canPrintDiscount = false,
  canPrintBigDiscount = false,
  onPrintProduct,
  onPrintDiscount,
  onPrintBigDiscount,
}: LabelPrintCardProps) {
  return (
    <Card style={styles.card} mode="contained">
      <Card.Content style={styles.content}>
        <View style={styles.actions}>
          <Button
            compact
            mode="contained"
            icon="printer-outline"
            onPress={onPrintProduct}
            loading={isPrintingProduct}
            disabled={!onPrintProduct || isPrintingProduct}
            style={styles.button}
          >
            Regular
          </Button>
          <Button
            compact
            mode="contained-tonal"
            icon="sale-outline"
            onPress={onPrintDiscount}
            loading={isPrintingDiscount}
            disabled={!onPrintDiscount || !canPrintDiscount || isPrintingDiscount}
            style={styles.button}
          >
            Discount
          </Button>
          <Button
            compact
            mode="outlined"
            icon="post-outline"
            onPress={onPrintBigDiscount}
            loading={isPrintingBigDiscount}
            disabled={!onPrintBigDiscount || !canPrintBigDiscount || isPrintingBigDiscount}
            style={styles.button}
          >
            Large
          </Button>
        </View>
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 8,
    backgroundColor: "#FFFDF7",
  },
  content: {
    paddingVertical: 8,
  },
  actions: {
    flexDirection: "row",
    gap: 8,
  },
  button: {
    flex: 1,
  },
});
