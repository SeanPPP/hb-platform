import { StyleSheet, TextInput, View } from "react-native";
import { Button, IconButton, Text } from "react-native-paper";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import {
  getSetUnitEquivalent,
  isCodeAddDraftValid,
  parseOptionalPrice,
} from "@/modules/product-maintenance/product-query-presentation";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

interface CodeAddSheetProps {
  visible: boolean;
  codeType: "set" | "multi";
  barcode: string;
  retailPrice: string;
  /** 主条码当前分店零售价，用于套装「相当于约 N 件」参考。 */
  unitRetailPrice?: number | null;
  onChangeBarcode: (value: string) => void;
  onChangeRetailPrice: (value: string) => void;
  onScan: () => void;
  onDismiss: () => void;
  onSubmit: () => void;
}

/** 新增套装 / 多码条码面板；提交仍走页面原有 handleConfirmCodeAdd。 */
export function CodeAddSheet({
  visible,
  codeType,
  barcode,
  retailPrice,
  unitRetailPrice,
  onChangeBarcode,
  onChangeRetailPrice,
  onScan,
  onDismiss,
  onSubmit,
}: CodeAddSheetProps) {
  const { t } = useAppTranslation(["productQuery", "common"]);
  const isSet = codeType === "set";
  const equivalent = isSet ? getSetUnitEquivalent(parseOptionalPrice(retailPrice), unitRetailPrice) : null;
  const hasUnitPrice = unitRetailPrice != null && Number.isFinite(unitRetailPrice) && unitRetailPrice > 0;
  const canSubmit = isCodeAddDraftValid(codeType, barcode, retailPrice);

  return (
    <BusinessSheet
      visible={visible}
      title={isSet ? t("setCode.addTitle") : t("multiCode.addTitle")}
      onDismiss={onDismiss}
      footer={(
        <View style={styles.footer}>
          <Button mode="outlined" onPress={onDismiss} style={styles.footerButton} contentStyle={styles.buttonContent}>
            {t("common:actions.cancel")}
          </Button>
          <Button
            mode="contained"
            onPress={onSubmit}
            disabled={!canSubmit}
            style={styles.footerButton}
            contentStyle={styles.buttonContent}
          >
            {t("codeAdd.submit")}
          </Button>
        </View>
      )}
    >
      <View style={styles.field}>
        <Text style={styles.label}>{isSet ? t("setCode.title") : t("multiCode.title")}</Text>
        <View style={styles.inputRow}>
          <TextInput
            style={[styles.input, styles.inputFlex]}
            value={barcode}
            onChangeText={onChangeBarcode}
            placeholder={t("codeAdd.barcodePlaceholder")}
            placeholderTextColor="#98A2B3"
            autoFocus
            autoCapitalize="none"
            autoCorrect={false}
            selectTextOnFocus
          />
          <IconButton
            icon="barcode-scan"
            accessibilityLabel={t("codeAdd.scan")}
            mode="contained-tonal"
            containerColor="#EAF2FF"
            iconColor={HB_COLORS.action}
            size={22}
            onPress={onScan}
            style={styles.scanButton}
          />
        </View>
      </View>

      {isSet ? (
        <View style={styles.field}>
          <Text style={styles.label}>{t("codeAdd.setRetail")}</Text>
          <View style={styles.inputRow}>
            <TextInput
              style={[styles.input, styles.priceInput]}
              value={retailPrice}
              onChangeText={onChangeRetailPrice}
              placeholder="0.00"
              placeholderTextColor="#98A2B3"
              keyboardType="decimal-pad"
            />
            {hasUnitPrice ? (
              <View style={styles.reference}>
                <Text style={styles.referenceText}>
                  {t("codeAdd.unitPrice", { price: unitRetailPrice.toFixed(2) })}
                </Text>
                {equivalent != null ? (
                  <Text style={styles.referenceStrong}>
                    {t("codeAdd.equivalent", { count: equivalent })}
                  </Text>
                ) : null}
              </View>
            ) : null}
          </View>
        </View>
      ) : (
        <Text style={styles.hint}>{t("multiCode.followMain")}</Text>
      )}
    </BusinessSheet>
  );
}

const styles = StyleSheet.create({
  field: {
    gap: 6,
  },
  label: {
    fontSize: 12,
    fontWeight: "600",
    color: HB_COLORS.textSecondary,
  },
  inputRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.xs,
  },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderColor: HB_COLORS.outline,
    borderRadius: HB_RADIUS.control,
    paddingHorizontal: HB_SPACING.sm,
    fontSize: 16,
    color: HB_COLORS.textPrimary,
    backgroundColor: HB_COLORS.white,
    fontVariant: ["tabular-nums"],
  },
  inputFlex: {
    flex: 1,
    minWidth: 0,
  },
  priceInput: {
    width: 128,
  },
  scanButton: {
    width: 48,
    height: 48,
    margin: 0,
    borderRadius: HB_RADIUS.control,
  },
  reference: {
    flex: 1,
    minWidth: 0,
    gap: 2,
  },
  referenceText: {
    fontSize: 12,
    color: HB_COLORS.textSecondary,
    fontVariant: ["tabular-nums"],
  },
  referenceStrong: {
    fontSize: 13,
    fontWeight: "700",
    color: HB_COLORS.action,
    fontVariant: ["tabular-nums"],
  },
  hint: {
    fontSize: 12,
    color: HB_COLORS.textSecondary,
  },
  footer: {
    flexDirection: "row",
    gap: HB_SPACING.xs,
  },
  footerButton: {
    flex: 1,
    borderRadius: HB_RADIUS.control,
  },
  buttonContent: {
    minHeight: 44,
  },
});
