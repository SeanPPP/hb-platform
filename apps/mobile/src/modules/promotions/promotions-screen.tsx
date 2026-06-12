import { useCallback, useEffect, useMemo, useState } from "react";
import { RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  Divider,
  IconButton,
  Modal,
  Portal,
  Snackbar,
  Switch,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import {
  copyPromotionToStore,
  createPromotion,
  fetchPromotionDetail,
  fetchPromotions,
  setPromotionEnabled,
  updatePromotion,
} from "@/modules/promotions/api";
import type {
  PromotionDetail,
  PromotionFormValues,
  PromotionListItem,
  PromotionProductItem,
  PromotionScopeType,
} from "@/modules/promotions/types";
import type { Store } from "@/modules/shop/types";
import { useStores } from "@/modules/shop/use-stores";
import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

const PAGE_SIZE = 20;

const EMPTY_FORM: PromotionFormValues = {
  name: "",
  description: "",
  storeCode: "",
  effectiveStart: "",
  effectiveEnd: "",
  isEnabled: true,
  isExclusive: true,
  priority: 0,
  applyQuantity: 0,
  fixedPrice: 0,
  maxApplicationsPerOrder: null,
  products: [{ productCode: "", unitWeight: 1 }],
};

function scopeTone(scopeType: PromotionScopeType | null) {
  if (scopeType === "StoreOnly") {
    return { backgroundColor: "#E6F4FF", textColor: "#0958D9" };
  }
  if (scopeType === "MultiStore") {
    return { backgroundColor: "#FFF7E6", textColor: "#AD6800" };
  }
  return { backgroundColor: "#F6FFED", textColor: "#237804" };
}

function toNumberInput(value: number | string | null | undefined) {
  return value == null ? "" : String(value);
}

function formatDate(value?: string) {
  if (!value) {
    return "--";
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString();
}

function createDefaultForm(storeCode: string): PromotionFormValues {
  const now = new Date();
  const end = new Date(now);
  end.setDate(end.getDate() + 30);
  return {
    ...EMPTY_FORM,
    storeCode,
    effectiveStart: now.toISOString(),
    effectiveEnd: end.toISOString(),
    products: [{ productCode: "", unitWeight: 1 }],
  };
}

function formFromDetail(detail: PromotionDetail, storeCode: string): PromotionFormValues {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description,
    storeCode,
    effectiveStart: detail.effectiveStart,
    effectiveEnd: detail.effectiveEnd,
    isEnabled: detail.isEnabled,
    isExclusive: detail.isExclusive,
    priority: detail.priority,
    applyQuantity: detail.applyQuantity,
    fixedPrice: detail.fixedPrice,
    maxApplicationsPerOrder: detail.maxApplicationsPerOrder ?? null,
    products: detail.products.length ? detail.products : [{ productCode: "", unitWeight: 1 }],
  };
}

function PromotionScopeTag({
  scopeType,
  label,
}: {
  scopeType: PromotionScopeType | null;
  label: string;
}) {
  const tone = scopeTone(scopeType);
  return (
    <Chip
      compact
      style={[styles.scopeChip, { backgroundColor: tone.backgroundColor }]}
      textStyle={[styles.scopeChipText, { color: tone.textColor }]}
    >
      {label}
    </Chip>
  );
}

export function PromotionsScreen() {
  const { t, language } = useAppTranslation(["promotions", "common"]);
  const {
    stores,
    selectedStore,
    selectedStoreCode,
    isLoading: storesLoading,
    selectStore,
  } = useStores();
  const [storePickerVisible, setStorePickerVisible] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<PromotionListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [formVisible, setFormVisible] = useState(false);
  const [formValues, setFormValues] = useState<PromotionFormValues>(EMPTY_FORM);
  const [saving, setSaving] = useState(false);
  const [snackbar, setSnackbar] = useState("");
  const [copySource, setCopySource] = useState<PromotionListItem | null>(null);

  const selectedStoreLabel = selectedStore?.storeName || selectedStoreCode || t("store.empty");
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE));

  const getErrorMessage = useCallback(
    (error: unknown, fallbackKey: string) =>
      resolveLocalizedErrorMessage(error, {
        language,
        t,
        fallbackKey,
      }),
    [language, t]
  );

  const loadPromotions = useCallback(
    async (nextPage: number, options?: { refreshing?: boolean }) => {
      if (!selectedStoreCode) {
        setItems([]);
        setTotal(0);
        return;
      }

      if (options?.refreshing) {
        setRefreshing(true);
      } else {
        setLoading(true);
      }

      try {
        const result = await fetchPromotions({
          storeCode: selectedStoreCode,
          keyword,
          page: nextPage,
          pageSize: PAGE_SIZE,
        });
        setItems(result.items);
        setTotal(result.total);
        setPage(nextPage);
      } catch (error) {
        setSnackbar(getErrorMessage(error, "messages.loadFailed"));
      } finally {
        setLoading(false);
        setRefreshing(false);
      }
    },
    [getErrorMessage, keyword, selectedStoreCode]
  );

  useEffect(() => {
    void loadPromotions(1);
  }, [loadPromotions, selectedStoreCode]);

  const openCreate = useCallback(() => {
    if (!selectedStoreCode) {
      setSnackbar(t("messages.selectStoreFirst"));
      return;
    }
    setFormValues(createDefaultForm(selectedStoreCode));
    setFormVisible(true);
  }, [selectedStoreCode, t]);

  const openEdit = useCallback(
    async (item: PromotionListItem) => {
      if (!selectedStoreCode) {
        setSnackbar(t("messages.selectStoreFirst"));
        return;
      }
      if (!item.canEditInStoreScope) {
        setSnackbar(t("messages.copyBeforeEdit"));
        return;
      }
      setLoading(true);
      try {
        const detail = await fetchPromotionDetail(item.id, selectedStoreCode);
        if (!detail) {
          setSnackbar(t("messages.detailMissing"));
          return;
        }
        setFormValues(formFromDetail(detail, selectedStoreCode));
        setFormVisible(true);
      } catch (error) {
        setSnackbar(getErrorMessage(error, "messages.detailFailed"));
      } finally {
        setLoading(false);
      }
    },
    [getErrorMessage, selectedStoreCode, t]
  );

  const savePromotion = useCallback(async () => {
    if (!selectedStoreCode) {
      setSnackbar(t("messages.selectStoreFirst"));
      return;
    }
    if (!formValues.name.trim()) {
      setSnackbar(t("messages.nameRequired"));
      return;
    }

    setSaving(true);
    try {
      const payload = { ...formValues, storeCode: selectedStoreCode };
      if (formValues.id) {
        await updatePromotion(formValues.id, payload);
      } else {
        await createPromotion(payload);
      }
      setFormVisible(false);
      setSnackbar(t("messages.saved"));
      await loadPromotions(page);
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.saveFailed"));
    } finally {
      setSaving(false);
    }
  }, [formValues, getErrorMessage, loadPromotions, page, selectedStoreCode, t]);

  const copyToStore = useCallback(async () => {
    if (!copySource || !selectedStoreCode) {
      return;
    }
    setSaving(true);
    try {
      const detail = await copyPromotionToStore({
        sourcePromotionId: copySource.id,
        storeCode: selectedStoreCode,
      });
      setCopySource(null);
      setSnackbar(t("messages.copied"));
      await loadPromotions(1);
      if (detail) {
        setFormValues(formFromDetail(detail, selectedStoreCode));
        setFormVisible(true);
      }
    } catch (error) {
      setSnackbar(getErrorMessage(error, "messages.copyFailed"));
    } finally {
      setSaving(false);
    }
  }, [copySource, getErrorMessage, loadPromotions, selectedStoreCode, t]);

  const toggleEnabled = useCallback(
    async (item: PromotionListItem) => {
      if (!selectedStoreCode || !item.canEditInStoreScope) {
        setSnackbar(t("messages.copyBeforeEdit"));
        return;
      }
      try {
        await setPromotionEnabled(item.id, selectedStoreCode, !item.isEnabled);
        await loadPromotions(page);
      } catch (error) {
        setSnackbar(getErrorMessage(error, "messages.enableFailed"));
      }
    },
    [getErrorMessage, loadPromotions, page, selectedStoreCode, t]
  );

  const updateProduct = useCallback(
    (index: number, patch: Partial<PromotionProductItem>) => {
      setFormValues((current) => {
        const products = [...(current.products ?? [])];
        products[index] = { productCode: "", unitWeight: 1, ...products[index], ...patch };
        return { ...current, products };
      });
    },
    []
  );

  const removeProduct = useCallback((index: number) => {
    setFormValues((current) => {
      const products = [...(current.products ?? [])];
      products.splice(index, 1);
      return { ...current, products: products.length ? products : [{ productCode: "", unitWeight: 1 }] };
    });
  }, []);

  const scopeLabels = useMemo(
    () => ({
      StoreOnly: t("scope.storeOnly"),
      MultiStore: t("scope.multiStore"),
      Headquarters: t("scope.headquarters"),
    }),
    [t]
  );

  const renderCard = (item: PromotionListItem) => {
    const scopeLabel = item.scopeType ? scopeLabels[item.scopeType] : t("scope.unknown");
    return (
      <Card key={item.id} style={styles.card} mode="outlined">
        <Card.Title
          title={item.name || t("fields.unnamed")}
          subtitle={`${formatDate(item.effectiveStart)} - ${formatDate(item.effectiveEnd)}`}
          right={() => (
            <IconButton
              icon={item.isEnabled ? "toggle-switch" : "toggle-switch-off-outline"}
              disabled={!item.canEditInStoreScope}
              onPress={() => void toggleEnabled(item)}
            />
          )}
        />
        <Card.Content style={styles.cardContent}>
          <View style={styles.tagRow}>
            <PromotionScopeTag scopeType={item.scopeType} label={scopeLabel} />
            <Chip compact>{item.isEnabled ? t("status.enabled") : t("status.disabled")}</Chip>
            <Chip compact>{item.isExclusive ? t("status.exclusive") : t("status.stackable")}</Chip>
          </View>
          <View style={styles.metricRow}>
            <Text variant="bodyMedium">{t("fields.priorityValue", { value: item.priority })}</Text>
            <Text variant="bodyMedium">{t("fields.ruleValue", { count: item.applyQuantity, price: item.fixedPrice.toFixed(2) })}</Text>
          </View>
          <Text variant="bodySmall" style={styles.mutedText}>
            {t("fields.productsAndStores", { products: item.productsCount, stores: item.storesCount })}
          </Text>
          <View style={styles.cardActions}>
            {item.canEditInStoreScope ? (
              <Button mode="contained-tonal" icon="pencil" onPress={() => void openEdit(item)}>
                {t("actions.edit")}
              </Button>
            ) : (
              <Button mode="contained-tonal" icon="content-copy" onPress={() => setCopySource(item)}>
                {t("actions.copyToStore")}
              </Button>
            )}
          </View>
        </Card.Content>
      </Card>
    );
  };

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right"]}>
      <View style={styles.header}>
        <View style={styles.headerText}>
          <Text variant="headlineSmall">{t("title")}</Text>
          <Text variant="bodyMedium" style={styles.mutedText}>
            {t("subtitle", { store: selectedStoreLabel })}
          </Text>
        </View>
        <Button mode="contained" icon="plus" onPress={openCreate} disabled={!selectedStoreCode}>
          {t("actions.create")}
        </Button>
      </View>

      <View style={styles.filters}>
        <Button mode="outlined" icon="storefront-outline" onPress={() => setStorePickerVisible(true)}>
          {selectedStoreLabel}
        </Button>
        <TextInput
          mode="outlined"
          dense
          value={keyword}
          placeholder={t("filters.keyword")}
          onChangeText={setKeyword}
          style={styles.searchInput}
          right={<TextInput.Icon icon="magnify" onPress={() => void loadPromotions(1)} />}
          onSubmitEditing={() => void loadPromotions(1)}
        />
      </View>

      {loading && !refreshing ? (
        <ActivityIndicator style={styles.loading} />
      ) : (
        <ScrollView
          refreshControl={
            <RefreshControl refreshing={refreshing} onRefresh={() => void loadPromotions(page, { refreshing: true })} />
          }
          contentContainerStyle={styles.content}
        >
          {!selectedStoreCode ? (
            <EmptyState title={t("empty.selectStoreTitle")} description={t("empty.selectStoreDescription")} />
          ) : items.length === 0 ? (
            <EmptyState title={t("empty.noDataTitle")} description={t("empty.noDataDescription")} />
          ) : (
            items.map(renderCard)
          )}
          {items.length > 0 ? (
            <View style={styles.pagination}>
              <Button disabled={page <= 1} onPress={() => void loadPromotions(page - 1)}>
                {t("pagination.previous")}
              </Button>
              <Text variant="bodyMedium">{t("pagination.page", { page, pageCount })}</Text>
              <Button disabled={page >= pageCount} onPress={() => void loadPromotions(page + 1)}>
                {t("pagination.next")}
              </Button>
            </View>
          ) : null}
        </ScrollView>
      )}

      <StorePickerModal
        visible={storePickerVisible}
        stores={stores}
        selectedStoreCode={selectedStoreCode}
        title={t("common:labels.selectStore")}
        cancelLabel={t("common:actions.cancel")}
        onDismiss={() => setStorePickerVisible(false)}
        onSelectStore={async (store: Store | null) => {
          await selectStore(store);
          setStorePickerVisible(false);
          setPage(1);
        }}
      />

      <PromotionFormModal
        visible={formVisible}
        formValues={formValues}
        saving={saving}
        storeLabel={selectedStoreLabel}
        t={t}
        onChange={setFormValues}
        onDismiss={() => setFormVisible(false)}
        onSave={() => void savePromotion()}
        onAddProduct={() =>
          setFormValues((current) => ({
            ...current,
            products: [...(current.products ?? []), { productCode: "", unitWeight: 1 }],
          }))
        }
        onRemoveProduct={removeProduct}
        onUpdateProduct={updateProduct}
      />

      <Portal>
        <Modal
          visible={Boolean(copySource)}
          onDismiss={() => setCopySource(null)}
          contentContainerStyle={styles.modal}
        >
          <Text variant="titleLarge">{t("copy.title")}</Text>
          <Text variant="bodyMedium" style={styles.modalText}>
            {t("copy.description", { name: copySource?.name ?? "" })}
          </Text>
          <View style={styles.modalActions}>
            <Button onPress={() => setCopySource(null)}>{t("common:actions.cancel")}</Button>
            <Button mode="contained" loading={saving} onPress={() => void copyToStore()}>
              {t("actions.copyToStore")}
            </Button>
          </View>
        </Modal>
      </Portal>

      <Snackbar visible={Boolean(snackbar)} onDismiss={() => setSnackbar("")} duration={4000}>
        {snackbar}
      </Snackbar>
    </SafeAreaView>
  );
}

function PromotionFormModal({
  visible,
  formValues,
  saving,
  storeLabel,
  t,
  onChange,
  onDismiss,
  onSave,
  onAddProduct,
  onRemoveProduct,
  onUpdateProduct,
}: {
  visible: boolean;
  formValues: PromotionFormValues;
  saving: boolean;
  storeLabel: string;
  t: (key: string, options?: Record<string, unknown>) => string;
  onChange: (values: PromotionFormValues) => void;
  onDismiss: () => void;
  onSave: () => void;
  onAddProduct: () => void;
  onRemoveProduct: (index: number) => void;
  onUpdateProduct: (index: number, patch: Partial<PromotionProductItem>) => void;
}) {
  const products = formValues.products ?? [];
  return (
    <Portal>
      <Modal visible={visible} onDismiss={onDismiss} contentContainerStyle={styles.formModal}>
        <ScrollView keyboardShouldPersistTaps="handled">
          <Text variant="titleLarge" style={styles.formTitle}>
            {formValues.id ? t("form.editTitle") : t("form.createTitle")}
          </Text>
          <Text variant="bodyMedium" style={styles.mutedText}>
            {t("form.storeReadonly", { store: storeLabel })}
          </Text>
          <TextInput
            mode="outlined"
            label={t("fields.name")}
            value={formValues.name}
            onChangeText={(name) => onChange({ ...formValues, name })}
            style={styles.input}
          />
          <TextInput
            mode="outlined"
            label={t("fields.description")}
            value={formValues.description ?? ""}
            onChangeText={(description) => onChange({ ...formValues, description })}
            style={styles.input}
          />
          <View style={styles.twoColumn}>
            <TextInput
              mode="outlined"
              label={t("fields.effectiveStart")}
              value={formValues.effectiveStart ?? ""}
              onChangeText={(effectiveStart) => onChange({ ...formValues, effectiveStart })}
              style={styles.flexInput}
            />
            <TextInput
              mode="outlined"
              label={t("fields.effectiveEnd")}
              value={formValues.effectiveEnd ?? ""}
              onChangeText={(effectiveEnd) => onChange({ ...formValues, effectiveEnd })}
              style={styles.flexInput}
            />
          </View>
          <View style={styles.twoColumn}>
            <TextInput
              mode="outlined"
              keyboardType="numeric"
              label={t("fields.applyQuantity")}
              value={toNumberInput(formValues.applyQuantity)}
              onChangeText={(value) => onChange({ ...formValues, applyQuantity: Number(value) || 0 })}
              style={styles.flexInput}
            />
            <TextInput
              mode="outlined"
              keyboardType="numeric"
              label={t("fields.fixedPrice")}
              value={toNumberInput(formValues.fixedPrice)}
              onChangeText={(value) => onChange({ ...formValues, fixedPrice: Number(value) || 0 })}
              style={styles.flexInput}
            />
          </View>
          <TextInput
            mode="outlined"
            keyboardType="numeric"
            label={t("fields.priority")}
            value={toNumberInput(formValues.priority)}
            onChangeText={(value) => onChange({ ...formValues, priority: Number(value) || 0 })}
            style={styles.input}
          />
          <Text variant="bodySmall" style={styles.mutedText}>
            {t("form.priorityHint")}
          </Text>
          <View style={styles.switchRow}>
            <Text>{t("fields.enabled")}</Text>
            <Switch value={formValues.isEnabled ?? true} onValueChange={(isEnabled) => onChange({ ...formValues, isEnabled })} />
          </View>
          <View style={styles.switchRow}>
            <Text>{t("fields.exclusive")}</Text>
            <Switch value={formValues.isExclusive ?? true} onValueChange={(isExclusive) => onChange({ ...formValues, isExclusive })} />
          </View>
          <Divider style={styles.divider} />
          <View style={styles.productHeader}>
            <Text variant="titleMedium">{t("form.products")}</Text>
            <Button icon="plus" onPress={onAddProduct}>
              {t("actions.addProduct")}
            </Button>
          </View>
          {products.map((product, index) => (
            <View key={`${index}-${product.id ?? ""}`} style={styles.productRow}>
              <TextInput
                mode="outlined"
                label={t("fields.productCode")}
                value={product.productCode ?? ""}
                onChangeText={(productCode) => onUpdateProduct(index, { productCode })}
                style={styles.productCodeInput}
              />
              <TextInput
                mode="outlined"
                keyboardType="numeric"
                label={t("fields.unitWeight")}
                value={toNumberInput(product.unitWeight)}
                onChangeText={(value) => onUpdateProduct(index, { unitWeight: Number(value) || 1 })}
                style={styles.weightInput}
              />
              <IconButton icon="delete-outline" onPress={() => onRemoveProduct(index)} />
            </View>
          ))}
          <View style={styles.modalActions}>
            <Button onPress={onDismiss}>{t("common:actions.cancel")}</Button>
            <Button mode="contained" loading={saving} onPress={onSave}>
              {t("common:actions.save")}
            </Button>
          </View>
        </ScrollView>
      </Modal>
    </Portal>
  );
}

const styles = StyleSheet.create({
  safeArea: { flex: 1, backgroundColor: "#F7F8FA" },
  header: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
    padding: 16,
  },
  headerText: { flex: 1, minWidth: 0 },
  filters: {
    flexDirection: "row",
    gap: 8,
    paddingHorizontal: 16,
    paddingBottom: 8,
  },
  searchInput: { flex: 1, backgroundColor: "#FFFFFF" },
  content: { gap: 12, padding: 16, paddingBottom: 32 },
  loading: { marginTop: 48 },
  card: { backgroundColor: "#FFFFFF", borderRadius: 8 },
  cardContent: { gap: 10 },
  tagRow: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  scopeChip: { borderRadius: 8 },
  scopeChipText: { fontWeight: "700" },
  metricRow: { flexDirection: "row", flexWrap: "wrap", gap: 12 },
  mutedText: { color: "#666666" },
  cardActions: { flexDirection: "row", justifyContent: "flex-end" },
  pagination: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "center",
    gap: 8,
    marginTop: 8,
  },
  modal: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 8,
    padding: 20,
    width: "88%",
  },
  modalText: { marginTop: 8 },
  modalActions: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: 8,
    marginTop: 16,
  },
  formModal: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 8,
    maxHeight: "90%",
    padding: 20,
    width: "92%",
  },
  formTitle: { marginBottom: 8 },
  input: { backgroundColor: "#FFFFFF", marginTop: 10 },
  twoColumn: { flexDirection: "row", gap: 8, marginTop: 10 },
  flexInput: { backgroundColor: "#FFFFFF", flex: 1 },
  switchRow: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    marginTop: 12,
  },
  divider: { marginVertical: 16 },
  productHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  productRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 8,
    marginTop: 8,
  },
  productCodeInput: { backgroundColor: "#FFFFFF", flex: 1 },
  weightInput: { backgroundColor: "#FFFFFF", width: 96 },
});
