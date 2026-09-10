import { forwardRef, useEffect, useRef, useState } from "react";
import {
  Keyboard,
  Platform,
  ScrollView,
  StyleSheet,
  TextInput,
  useWindowDimensions,
  View,
  type TextInputProps,
} from "react-native";
import { Button, IconButton, Modal, Switch, Text } from "react-native-paper";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import type { CreateProductFormValues } from "@/modules/product-maintenance/create-product-validation";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS as C } from "@/shared/theme/tokens";

interface Props {
  visible: boolean;
  values: CreateProductFormValues;
  supplierLabel: string | null;
  suppliersLoading: boolean;
  hasSuppliers: boolean;
  saving: boolean;
  generating: boolean;
  onChange: (patch: Partial<CreateProductFormValues>) => void;
  onSelectSupplier: () => void;
  onReloadSuppliers: () => void;
  onGenerate: () => void;
  onScan: () => void;
  onDismiss: () => void;
  onSubmit: () => void;
}

export function CreateProductDialog(props: Props) {
  const { t } = useAppTranslation(["productQuery", "common"]);
  const { height, width } = useWindowDimensions();
  const insets = useSafeAreaInsets();
  const [keyboardHeight, setKeyboardHeight] = useState(0);
  const nameRef = useRef<TextInput>(null);
  const itemRef = useRef<TextInput>(null);
  const barcodeRef = useRef<TextInput>(null);
  const purchaseRef = useRef<TextInput>(null);
  const retailRef = useRef<TextInput>(null);
  const busy = props.saving || props.generating;
  const restricted = props.values.localSupplierCode.trim() === "200";

  useEffect(() => {
    if (!props.visible) {
      setKeyboardHeight(0);
      return;
    }
    // 键盘占用区域从弹窗可用高度中扣除，标题和操作栏始终留在滚动区域外。
    const show = Keyboard.addListener(
      Platform.OS === "ios" ? "keyboardWillChangeFrame" : "keyboardDidShow",
      (event) => {
        setKeyboardHeight(Math.max(0, height - event.endCoordinates.screenY));
      },
    );
    const hide = Keyboard.addListener(
      Platform.OS === "ios" ? "keyboardWillHide" : "keyboardDidHide",
      () => setKeyboardHeight(0),
    );
    return () => {
      show.remove();
      hide.remove();
    };
  }, [height, props.visible]);

  const field = (
    key:
      | "productName"
      | "itemNumber"
      | "barcode"
      | "purchasePrice"
      | "retailPrice",
  ) => ({
    label: t(`createProduct.fields.${key}`),
    value: props.values[key],
    editable: !busy,
    onChangeText: (value: string) => props.onChange({ [key]: value }),
  });

  return (
    <Modal
      visible={props.visible}
      dismissable={!busy}
      onDismiss={props.onDismiss}
      style={{
        marginTop: insets.top + 12,
        marginBottom: Math.max(keyboardHeight, insets.bottom) + 12,
      }}
      contentContainerStyle={[
        styles.dialog,
        {
          width: Math.min(width - 24, 520),
          maxHeight:
            height - insets.top - Math.max(keyboardHeight, insets.bottom) - 24,
        },
      ]}
    >
      <View style={styles.header}>
        <View style={styles.titleBlock}>
          <Text variant="titleLarge" style={styles.title}>
            {t("createProduct.title")}
          </Text>
          <Text style={styles.subtitle}>{t("createProduct.formHint")}</Text>
        </View>
        <IconButton
          icon="close"
          size={21}
          disabled={busy}
          onPress={props.onDismiss}
          accessibilityLabel={t("common:actions.cancel")}
          style={styles.close}
        />
      </View>
      <ScrollView
        style={styles.scroll}
        contentContainerStyle={styles.body}
        keyboardShouldPersistTaps="handled"
        keyboardDismissMode="on-drag"
        showsVerticalScrollIndicator
      >
        <View style={styles.field}>
          <Text style={styles.label}>{t("createProduct.fields.supplier")}</Text>
          <Button
            mode="outlined"
            icon="chevron-down"
            disabled={busy || props.suppliersLoading}
            onPress={() => {
              Keyboard.dismiss();
              if (props.hasSuppliers) props.onSelectSupplier();
              else props.onReloadSuppliers();
            }}
            style={[styles.supplier, restricted && styles.restrictedBorder]}
            contentStyle={styles.supplierContent}
            labelStyle={styles.supplierLabel}
          >
            {props.suppliersLoading
              ? t("createProduct.messages.suppliersLoading")
              : props.supplierLabel ||
                t(
                  props.hasSuppliers
                    ? "createProduct.selectSupplier"
                    : "createProduct.reloadSuppliers",
                )}
          </Button>
          {restricted ? (
            <Text accessibilityRole="alert" style={styles.warning}>
              {t("createProduct.messages.supplierRestricted")}
            </Text>
          ) : null}
        </View>
        <Field
          ref={nameRef}
          {...field("productName")}
          placeholder={t("createProduct.namePlaceholder")}
          returnKeyType="next"
          onSubmitEditing={() => itemRef.current?.focus()}
        />
        <Field
          ref={itemRef}
          {...field("itemNumber")}
          placeholder={t("createProduct.itemPlaceholder")}
          autoCapitalize="characters"
          autoCorrect={false}
          returnKeyType="next"
          onSubmitEditing={() => barcodeRef.current?.focus()}
        />
        <View style={styles.field}>
          <Text style={styles.label}>{t("createProduct.fields.barcode")}</Text>
          <View style={styles.barcodeRow}>
            <Field
              ref={barcodeRef}
              {...field("barcode")}
              hideLabel
              placeholder={t("createProduct.barcodePlaceholder")}
              autoCapitalize="none"
              autoCorrect={false}
              returnKeyType="next"
              onSubmitEditing={() => purchaseRef.current?.focus()}
            />
          </View>
          <View style={styles.barcodeActions}>
            <Button
              mode="contained-tonal"
              icon="camera-outline"
              compact
              disabled={busy || restricted}
              onPress={() => {
                Keyboard.dismiss();
                props.onScan();
              }}
              style={styles.generate}
              contentStyle={styles.generateContent}
              labelStyle={styles.generateLabel}
            >
              {t("createProduct.cameraScan")}
            </Button>
            <Button
              mode="contained-tonal"
              icon="barcode"
              compact
              loading={props.generating}
              disabled={
                busy || restricted || !props.values.localSupplierCode.trim()
              }
              onPress={props.onGenerate}
              style={styles.generate}
              contentStyle={styles.generateContent}
              labelStyle={styles.generateLabel}
              accessibilityLabel={t("createProduct.generateBarcode")}
            >
              {t("createProduct.generateShort")}
            </Button>
          </View>
        </View>
        <View style={styles.priceRow}>
          <Field
            ref={purchaseRef}
            {...field("purchasePrice")}
            placeholder="0.00"
            keyboardType="decimal-pad"
            prefix="$"
          />
          <Field
            ref={retailRef}
            {...field("retailPrice")}
            placeholder="0.00"
            keyboardType="decimal-pad"
            prefix="$"
          />
        </View>
        <View style={styles.settings}>
          <View style={styles.settingRow}>
            <Text style={styles.settingLabel}>
              {t("createProduct.specialShort")}
            </Text>
            <Switch
              value={props.values.isSpecialProduct}
              disabled={busy}
              color={C.brand}
              onValueChange={(isSpecialProduct) =>
                props.onChange({ isSpecialProduct })
              }
              accessibilityLabel={t("createProduct.specialShort")}
            />
          </View>
          <View style={[styles.settingRow, styles.settingDivider]}>
            <Text style={styles.settingLabel}>
              {t("createProduct.autoPriceShort")}
            </Text>
            <Switch
              value={props.values.isAutoPricing}
              disabled={busy}
              color={C.brand}
              onValueChange={(isAutoPricing) =>
                props.onChange({ isAutoPricing })
              }
              accessibilityLabel={t("createProduct.autoPriceShort")}
            />
          </View>
        </View>
      </ScrollView>
      <View style={styles.footer}>
        {keyboardHeight > 0 ? (
          <Button
            compact
            onPress={Keyboard.dismiss}
            style={styles.keyboardDone}
          >
            {t("createProduct.keyboardDone")}
          </Button>
        ) : null}
        <View style={styles.actions}>
          <Button
            mode="outlined"
            disabled={busy}
            onPress={props.onDismiss}
            style={styles.cancel}
            contentStyle={styles.actionContent}
          >
            {t("common:actions.cancel")}
          </Button>
          <Button
            mode="contained"
            loading={props.saving}
            disabled={busy || restricted}
            onPress={props.onSubmit}
            style={styles.submit}
            buttonColor={C.brand}
            contentStyle={styles.actionContent}
          >
            {t("createProduct.action")}
          </Button>
        </View>
      </View>
    </Modal>
  );
}

const Field = forwardRef<
  TextInput,
  TextInputProps & { label: string; hideLabel?: boolean; prefix?: string }
>(function Field({ label, hideLabel, prefix, ...props }, ref) {
  const [focused, setFocused] = useState(false);
  return (
    <View style={styles.field}>
      {!hideLabel ? <Text style={styles.label}>{label}</Text> : null}
      <View
        style={[
          styles.inputFrame,
          focused && styles.inputFocused,
          props.editable === false && styles.inputDisabled,
        ]}
      >
        {prefix ? <Text style={styles.prefix}>{prefix}</Text> : null}
        <TextInput
          {...props}
          ref={ref}
          accessibilityLabel={label}
          placeholderTextColor="#98A2B3"
          style={styles.input}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
        />
      </View>
    </View>
  );
});

const styles = StyleSheet.create({
  dialog: {
    alignSelf: "center",
    borderRadius: 20,
    backgroundColor: C.white,
    overflow: "hidden",
  },
  header: {
    padding: 20,
    paddingBottom: 16,
    flexDirection: "row",
    alignItems: "center",
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: C.outlineMuted,
  },
  titleBlock: { flex: 1, gap: 4 },
  title: { color: C.textPrimary, fontWeight: "700", fontSize: 21 },
  subtitle: { color: C.textSecondary, fontSize: 12 },
  close: { margin: -6, marginLeft: 8 },
  scroll: { flexShrink: 1, minHeight: 0 },
  body: { padding: 20, paddingTop: 16, gap: 14 },
  field: { gap: 6, flexShrink: 1, flexGrow: 1, minWidth: 0 },
  label: { color: C.textSecondary, fontSize: 12, fontWeight: "600" },
  supplier: {
    borderRadius: 10,
    borderColor: C.outline,
    backgroundColor: C.surface,
  },
  supplierContent: {
    minHeight: 46,
    flexDirection: "row-reverse",
    justifyContent: "space-between",
  },
  supplierLabel: {
    flex: 1,
    textAlign: "left",
    color: C.textPrimary,
    fontSize: 15,
    marginHorizontal: 12,
    marginVertical: 10,
  },
  restrictedBorder: { borderColor: C.warning },
  warning: {
    color: C.warning,
    backgroundColor: "#FFFAEB",
    borderRadius: 8,
    padding: 10,
    fontSize: 12,
    lineHeight: 18,
  },
  inputFrame: {
    minHeight: 46,
    borderWidth: 1,
    borderColor: C.outline,
    borderRadius: 10,
    backgroundColor: C.white,
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: 12,
  },
  inputFocused: { borderColor: C.brand, backgroundColor: "#F8FBFF" },
  inputDisabled: { backgroundColor: C.surfaceMuted },
  input: {
    flex: 1,
    minWidth: 0,
    paddingVertical: 11,
    paddingHorizontal: 0,
    color: C.textPrimary,
    fontSize: 15,
  },
  prefix: { color: C.textSecondary, marginRight: 7, fontSize: 15 },
  barcodeRow: { flexDirection: "row", alignItems: "stretch", gap: 8 },
  barcodeActions: { flexDirection: "row", gap: 8, marginTop: 2 },
  generate: {
    flex: 1,
    borderRadius: 10,
    alignSelf: "flex-start",
    backgroundColor: "#EAF3FF",
  },
  generateContent: { minHeight: 46 },
  generateLabel: { fontSize: 13, color: C.action, marginHorizontal: 12 },
  priceRow: { flexDirection: "row", gap: 12 },
  settings: {
    backgroundColor: C.surfaceMuted,
    borderRadius: 12,
    paddingHorizontal: 12,
  },
  settingRow: {
    minHeight: 48,
    paddingVertical: 5,
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  settingLabel: { flex: 1, fontSize: 14, color: C.textPrimary },
  settingDivider: {
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: C.outline,
  },
  footer: {
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: C.outlineMuted,
    padding: 16,
    backgroundColor: C.white,
  },
  actions: { flexDirection: "row", gap: 12 },
  cancel: { borderRadius: 10, borderColor: C.outline, flex: 1 },
  submit: { borderRadius: 10, flex: 2 },
  actionContent: { minHeight: 46 },
  keyboardDone: { alignSelf: "flex-end", marginTop: -10, marginBottom: 4 },
});
