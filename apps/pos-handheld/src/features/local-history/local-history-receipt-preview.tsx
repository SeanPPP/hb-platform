import { useMemo } from "react";
import {
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";

import { receiptCode128Runs } from "./receipt-code128";
import { receiptQrMatrix } from "./receipt-qr-matrix";

import type { EscPosDocument } from "@/features/receipts/receipt-document";
import { posColors } from "@/ui/theme";

export type LocalHistoryReceiptDocument = EscPosDocument;

export function LocalHistoryReceiptPreview({
  document,
}: Readonly<{
  document: LocalHistoryReceiptDocument;
}>) {
  return (
    <ScrollView
      contentContainerStyle={styles.scrollContent}
      style={styles.scroll}
      testID="local-history-receipt-preview"
    >
      <View
        style={[
          styles.paper,
          document.paper === "80mm" && styles.paper80mm,
        ]}
        testID="local-history-receipt-paper"
      >
        {document.lines.map((line, index) => {
          if (line.kind === "barcode") {
            return (
              <ReceiptBarcode
                index={index}
                key={`barcode-${index}-${line.value}`}
                value={line.value}
              />
            );
          }
          if (line.kind === "qr") {
            return (
              <ReceiptQrCode
                index={index}
                key={`qr-${index}-${line.value}`}
                value={line.value}
              />
            );
          }
          if (line.kind === "feed") {
            return <View key={`feed-${index}`} style={styles.feed} />;
          }
          return (
            <Text
              key={`${line.kind}-${index}-${line.text}`}
              style={[
                styles.text,
                line.kind === "separator" && styles.separator,
                line.align === "center" && styles.textCenter,
                line.align === "right" && styles.textRight,
                line.bold && styles.textBold,
              ]}
              testID={`local-history-receipt-line-${index}`}
            >
              {line.text}
            </Text>
          );
        })}
      </View>
    </ScrollView>
  );
}

function ReceiptBarcode({
  index,
  value,
}: Readonly<{
  index: number;
  value: string;
}>) {
  const runs = useMemo(() => receiptCode128Runs(value), [value]);
  return (
    <View
      accessibilityLabel={`Receipt barcode ${value}`}
      accessibilityRole="image"
      style={styles.barcode}
      testID={`local-history-receipt-barcode-${index}`}
    >
      <View style={styles.barcodeBars}>
        {runs.map((run, runIndex) => (
          <View
            key={`${runIndex}-${run.bar ? "bar" : "space"}-${run.modules}`}
            style={[
              styles.barcodeRun,
              { flex: run.modules },
              run.bar && styles.barcodeRunDark,
            ]}
            testID={`local-history-receipt-barcode-run-${index}-${runIndex}`}
          />
        ))}
      </View>
      <Text selectable style={styles.barcodeValue}>
        {value}
      </Text>
    </View>
  );
}

function ReceiptQrCode({
  index,
  value,
}: Readonly<{
  index: number;
  value: string;
}>) {
  const matrix = useMemo(() => receiptQrMatrix(value), [value]);
  const moduleSize = Math.max(
    3,
    Math.floor(168 / (matrix.length + 8)),
  );
  return (
    <View
      accessibilityLabel={`Receipt QR code ${value}`}
      accessibilityRole="image"
      style={styles.qrContainer}
      testID={`local-history-receipt-qr-${index}`}
    >
      <View
        style={[
          styles.qrQuietZone,
          { padding: moduleSize * 4 },
        ]}
      >
        {matrix.map((row, rowIndex) => (
          <View
            key={`row-${rowIndex}`}
            style={styles.qrRow}
            testID={`local-history-receipt-qr-row-${index}-${rowIndex}`}
          >
            {row.map((dark, columnIndex) => (
            <View
              key={`${rowIndex}-${columnIndex}`}
              style={[
                styles.qrCell,
                { height: moduleSize, width: moduleSize },
                dark && styles.qrCellDark,
              ]}
              testID={`local-history-receipt-qr-cell-${index}-${rowIndex}-${columnIndex}`}
            />
            ))}
          </View>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  scroll: {
    flex: 1,
  },
  scrollContent: {
    alignItems: "center",
    padding: 14,
  },
  paper: {
    alignSelf: "center",
    width: "100%",
    maxWidth: 312,
    minWidth: 0,
    paddingHorizontal: 18,
    paddingVertical: 22,
    backgroundColor: "#FFFFFF",
    borderWidth: StyleSheet.hairlineWidth,
    borderColor: posColors.border,
    gap: 3,
  },
  paper80mm: {
    maxWidth: 390,
  },
  text: {
    maxWidth: "100%",
    color: "#111111",
    fontFamily: "Menlo",
    fontSize: 11,
    lineHeight: 15,
  },
  separator: {
    color: "#444444",
  },
  textCenter: {
    textAlign: "center",
  },
  textRight: {
    textAlign: "right",
  },
  textBold: {
    fontWeight: "700",
  },
  feed: {
    height: 12,
  },
  barcode: {
    alignItems: "center",
    maxWidth: "100%",
    marginVertical: 8,
  },
  barcodeBars: {
    flexDirection: "row",
    alignSelf: "center",
    width: "100%",
    maxWidth: 300,
    height: 48,
    paddingHorizontal: 10,
    backgroundColor: "#FFFFFF",
  },
  barcodeRun: {
    height: "100%",
    backgroundColor: "#FFFFFF",
  },
  barcodeRunDark: {
    backgroundColor: "#111111",
  },
  barcodeValue: {
    maxWidth: "100%",
    color: "#111111",
    fontFamily: "Menlo",
    fontSize: 10,
    lineHeight: 13,
    textAlign: "center",
  },
  qrContainer: {
    alignItems: "center",
    marginVertical: 10,
  },
  qrQuietZone: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
  },
  qrRow: {
    flexDirection: "row",
  },
  qrCell: {
    backgroundColor: "#FFFFFF",
  },
  qrCellDark: {
    backgroundColor: "#111111",
  },
});
