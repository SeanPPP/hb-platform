import { useMemo, useState } from "react";
import {
  FlatList,
  Keyboard,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  StyleSheet,
  TextInput,
  useWindowDimensions,
  View,
} from "react-native";
import {
  ActivityIndicator,
  Button,
  Icon,
  IconButton,
  Text,
} from "react-native-paper";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import type { LocalSupplierOption } from "@/modules/product-maintenance/types";
import { searchCreateSuppliers } from "@/modules/product-maintenance/supplier-search";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS as C } from "@/shared/theme/tokens";

interface Props {
  suppliers: LocalSupplierOption[];
  selectedCode: string;
  loading: boolean;
  disabled: boolean;
  onSelect: (supplier: LocalSupplierOption) => void;
  onDismiss: () => void;
  onReload: () => void;
}

// 父页面仅在可见时挂载，重新打开时清空查询，创建表单草稿仍由父页面保存。
export function CreateSupplierSheet(props: Props) {
  const { t, language } = useAppTranslation(["productQuery", "common"]);
  const [query, setQuery] = useState("");
  const { height } = useWindowDimensions();
  const insets = useSafeAreaInsets();
  const suppliers = useMemo(
    () => searchCreateSuppliers(props.suppliers, query, language),
    [props.suppliers, query, language],
  );
  const dismiss = () => {
    if (!props.disabled) {
      Keyboard.dismiss();
      props.onDismiss();
    }
  };
  return (
    <Modal
      transparent
      visible
      animationType="none"
      onRequestClose={dismiss}
      statusBarTranslucent
    >
      <KeyboardAvoidingView
        style={[styles.overlay, { paddingTop: insets.top + 12 }]}
        behavior={Platform.OS === "ios" ? "padding" : undefined}
      >
        <Pressable
          style={styles.backdrop}
          accessible={false}
          onPress={dismiss}
        />
        <View
          style={[
            styles.sheet,
            {
              height: Math.min(height * 0.82, 720),
              maxHeight: height - insets.top - 12,
              paddingBottom: Math.max(insets.bottom, 8),
            },
          ]}
          accessibilityViewIsModal
        >
          <View style={styles.handle} />
          <View style={styles.header}>
            <Text accessibilityRole="header" style={styles.title}>
              {t("createProduct.supplierPickerTitle")}
            </Text>
            <IconButton
              icon="close"
              accessibilityLabel={t("common:actions.close")}
              disabled={props.disabled}
              onPress={dismiss}
            />
          </View>
          <View style={styles.search}>
            <Icon source="magnify" size={22} color={C.textSecondary} />
            <TextInput
              style={styles.searchInput}
              value={query}
              onChangeText={setQuery}
              editable={!props.disabled}
              placeholder={t("createProduct.supplierSearch")}
              accessibilityLabel={t("createProduct.supplierSearch")}
              placeholderTextColor="#98A2B3"
              autoCorrect={false}
              autoCapitalize="none"
              returnKeyType="search"
              onSubmitEditing={Keyboard.dismiss}
            />
            {query ? (
              <IconButton
                icon="close-circle"
                size={19}
                accessibilityLabel={t("createProduct.clearSearch")}
                onPress={() => setQuery("")}
                disabled={props.disabled}
                style={styles.clear}
              />
            ) : null}
          </View>
          <View style={styles.caption}>
            <Text style={styles.muted}>
              {t("createProduct.supplierList", { count: suppliers.length })}
            </Text>
            <Text style={styles.muted}>{t("createProduct.nameSort")}</Text>
          </View>
          <FlatList
            data={suppliers}
            keyExtractor={(item) => item.supplierCode}
            style={styles.list}
            keyboardShouldPersistTaps="handled"
            keyboardDismissMode="on-drag"
            renderItem={({ item }) => (
              <Pressable
                disabled={props.disabled}
                accessibilityRole="button"
                accessibilityState={{
                  selected: item.supplierCode === props.selectedCode,
                  disabled: props.disabled,
                }}
                accessibilityLabel={`${item.supplierName}, ${item.supplierCode}`}
                onPress={() => {
                  Keyboard.dismiss();
                  props.onSelect(item);
                }}
                style={({ pressed }) => [
                  styles.row,
                  item.supplierCode === props.selectedCode && styles.selected,
                  pressed && styles.pressed,
                ]}
              >
                <View style={styles.rowText}>
                  <Text style={styles.name}>
                    {item.supplierName || item.supplierCode}
                  </Text>
                  <Text style={styles.code}>
                    {t("createProduct.supplierCode", {
                      code: item.supplierCode,
                    })}
                  </Text>
                </View>
                {item.supplierCode === "200" ? (
                  <Text style={styles.badge}>
                    {t("createProduct.adminOnly")}
                  </Text>
                ) : null}
                {item.supplierCode === props.selectedCode ? (
                  <Icon source="check-circle" size={24} color={C.brand} />
                ) : null}
              </Pressable>
            )}
            ListEmptyComponent={
              <View style={styles.empty}>
                {props.loading ? (
                  <ActivityIndicator />
                ) : (
                  <>
                    <Icon source="magnify" size={32} color={C.outline} />
                    <Text style={styles.muted}>
                      {t("createProduct.noSuppliers")}
                    </Text>
                    <Button
                      onPress={query ? () => setQuery("") : props.onReload}
                    >
                      {t(
                        query
                          ? "createProduct.clearSearch"
                          : "createProduct.reloadSuppliers",
                      )}
                    </Button>
                  </>
                )}
              </View>
            }
          />
          <View style={styles.footer}>
            <Text style={styles.muted}>
              {t("createProduct.supplierSelectHint")}
            </Text>
          </View>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}
const styles = StyleSheet.create({
  overlay: { flex: 1, justifyContent: "flex-end", alignItems: "center" },
  backdrop: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: "rgba(16,24,40,0.42)",
  },
  sheet: {
    width: "100%",
    maxWidth: 560,
    flexShrink: 1,
    backgroundColor: C.white,
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    overflow: "hidden",
  },
  handle: {
    width: 40,
    height: 4,
    borderRadius: 2,
    backgroundColor: C.outline,
    marginTop: 10,
    alignSelf: "center",
  },
  header: {
    paddingLeft: 20,
    paddingRight: 8,
    flexDirection: "row",
    alignItems: "center",
    minHeight: 64,
  },
  title: { flex: 1, color: C.textPrimary, fontSize: 21, fontWeight: "700" },
  search: {
    marginHorizontal: 20,
    flexDirection: "row",
    alignItems: "center",
    paddingLeft: 12,
    paddingRight: 4,
    minHeight: 46,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: C.outlineMuted,
    backgroundColor: C.surfaceMuted,
  },
  searchInput: {
    flex: 1,
    minWidth: 0,
    padding: 10,
    fontSize: 14,
    color: C.textPrimary,
  },
  clear: { margin: 0 },
  caption: {
    paddingHorizontal: 20,
    paddingVertical: 14,
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 8,
  },
  muted: { color: C.textSecondary, fontSize: 12 },
  list: { flex: 1 },
  row: {
    minHeight: 66,
    paddingVertical: 12,
    paddingHorizontal: 20,
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: C.outlineMuted,
  },
  rowText: { flex: 1, gap: 4 },
  name: { fontSize: 16, fontWeight: "600", color: C.textPrimary },
  code: { fontSize: 12, color: C.textSecondary },
  selected: { backgroundColor: "#EAF3FF" },
  pressed: { backgroundColor: C.surfaceMuted },
  badge: {
    color: C.warning,
    backgroundColor: "#FEF0C7",
    paddingHorizontal: 7,
    paddingVertical: 4,
    borderRadius: 5,
    fontSize: 11,
  },
  empty: { padding: 28, gap: 12, alignItems: "center" },
  footer: {
    padding: 14,
    alignItems: "center",
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: C.outlineMuted,
  },
});
