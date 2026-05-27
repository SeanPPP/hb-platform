import { useEffect, useMemo, useState } from "react";
import {
  RefreshControl,
  ScrollView,
  StyleSheet,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import {
  ActivityIndicator,
  Button,
  Card,
  DataTable,
  HelperText,
  Modal,
  Portal,
  SegmentedButtons,
  Snackbar,
  Surface,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import {
  SelectionListModal,
  type SelectionListItem,
} from "@/components/ui/SelectionListModal";
import { StorePickerModal } from "@/components/ui/StorePickerModal";
import {
  buildSeasonalCardYearOptions,
  formatSeasonalCardPriceOptionLabel,
  getSeasonalCardCatalogGroups,
  getSeasonalCardHistoryCardTypeOptions,
  getSeasonalCardLocalizedTypeLabel,
  isSeasonalCardCustomPriceOption,
  validateSeasonalCardSubmissionDraft,
} from "@/modules/seasonal-cards/form";
import {
  shouldEnableSeasonalCardCatalog,
  useSeasonalCardCatalog,
  useSeasonalCardSubmissionDetail,
  useSeasonalCardSubmissions,
  useSubmitSeasonalCardSubmission,
} from "@/modules/seasonal-cards/hooks";
import type {
  SeasonalCardCatalogItem,
  SeasonalCardSubmissionDraft,
  SeasonalCardSubmissionRecord,
  SeasonalCardType,
} from "@/modules/seasonal-cards/types";
import type { Store } from "@/modules/shop/types";
import { getDeviceBoundStoreCode } from "@/modules/shop/device-bound-store-filter";
import { useStores } from "@/modules/shop/use-stores";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";
import { useAuthStore } from "@/store/auth-store";
import { useCartStore } from "@/store/cart-store";

type ViewMode = "submit" | "history";
type HistoryDisplayMode = "list" | "table";
type PickerTarget = "formStore" | "historyStore" | null;
type YearPickerTarget = "formYear" | "historyYear" | null;
type CardTypePickerTarget = "formCardType" | "historyCardType" | null;

const PAGE_SIZE = 20;

function formatMoney(value?: number | null) {
  return value == null || Number.isNaN(value) ? "--" : `$${value.toFixed(2)}`;
}

function formatDateTime(value?: string | null, localeTag = "en-AU") {
  if (!value) {
    return "--";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString(localeTag, { hour12: false });
}

function getPageCount(total: number, pageSize: number) {
  return Math.max(1, Math.ceil(total / pageSize));
}

function getHistoryPriceKey(item: SeasonalCardSubmissionRecord) {
  return item.priceLabel || formatMoney(item.unitPrice);
}

function getHistoryPriceSortValue(priceLabel: string) {
  const numeric = Number(priceLabel.replace(/[^0-9.]/g, ""));
  if (Number.isFinite(numeric) && numeric > 0) {
    return numeric;
  }
  return Number.MAX_SAFE_INTEGER;
}

function buildHistoryTable(records: SeasonalCardSubmissionRecord[]) {
  const years = Array.from(
    new Set(
      records
        .map((item) => item.seasonYear)
        .filter((year): year is number => typeof year === "number")
    )
  ).sort((left, right) => left - right);

  const rows = new Map<
    string,
    {
      priceLabel: string;
      cells: Map<number, { quantity: number; submittedAt: string }>;
    }
  >();

  records.forEach((item) => {
    if (typeof item.seasonYear !== "number" || item.remainingQuantity == null) {
      return;
    }

    const priceLabel = getHistoryPriceKey(item);
    const row = rows.get(priceLabel) ?? {
      priceLabel,
      cells: new Map<number, { quantity: number; submittedAt: string }>(),
    };
    const current = row.cells.get(item.seasonYear);
    const nextSubmittedAt = item.submittedAt || "";
    if (!current || nextSubmittedAt >= current.submittedAt) {
      row.cells.set(item.seasonYear, {
        quantity: item.remainingQuantity,
        submittedAt: nextSubmittedAt,
      });
    }
    rows.set(priceLabel, row);
  });

  return {
    years,
    rows: Array.from(rows.values()).sort((left, right) => {
      const priceDelta =
        getHistoryPriceSortValue(left.priceLabel) -
        getHistoryPriceSortValue(right.priceLabel);
      return priceDelta || left.priceLabel.localeCompare(right.priceLabel);
    }),
  };
}

function FieldButton({
  label,
  value,
  placeholder,
  onPress,
  style,
}: {
  label: string;
  value?: string | null;
  placeholder: string;
  onPress: () => void;
  style?: StyleProp<ViewStyle>;
}) {
  return (
    <Surface style={[styles.fieldSurface, style]}>
      <Text variant="labelMedium" style={styles.fieldLabel}>
        {label}
      </Text>
      <Button
        compact
        mode="outlined"
        onPress={onPress}
        contentStyle={styles.fieldButtonContent}
        labelStyle={styles.fieldButtonLabel}
      >
        {value?.trim() ? value : placeholder}
      </Button>
    </Surface>
  );
}

function DetailLine({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.detailLine}>
      <Text variant="labelMedium" style={styles.detailLineLabel}>
        {label}
      </Text>
      <Text variant="bodyMedium" style={styles.detailLineValue}>
        {value}
      </Text>
    </View>
  );
}

export function SeasonalCardsScreen() {
  const { t, language } = useAppTranslation(["seasonalCards", "common"]);
  const localeTag = useMemo(() => resolveLocaleTag(language), [language]);
  const access = useAuthStore((state) => state.access);
  const selectedStore = useCartStore((state) => state.selectedStore);
  const { stores, selectedStoreCode, isDeviceMode } = useStores();
  const canView = access.canViewSeasonalCardRemaining;
  const canSubmit = access.canSubmitSeasonalCardRemaining;
  const yearOptions = useMemo(
    () => buildSeasonalCardYearOptions(new Date().getFullYear()),
    []
  );
  const [viewMode, setViewMode] = useState<ViewMode>(
    canSubmit ? "submit" : "history"
  );
  const [storePickerTarget, setStorePickerTarget] = useState<PickerTarget>(null);
  const [yearPickerTarget, setYearPickerTarget] = useState<YearPickerTarget>(null);
  const [cardTypePickerTarget, setCardTypePickerTarget] =
    useState<CardTypePickerTarget>(null);
  const [priceOptionPickerVisible, setPriceOptionPickerVisible] = useState(false);
  const [historyPageNumber, setHistoryPageNumber] = useState(1);
  const [historyDisplayMode, setHistoryDisplayMode] =
    useState<HistoryDisplayMode>("list");
  const [selectedSubmissionGuid, setSelectedSubmissionGuid] = useState<string | null>(null);
  const [submitAttempted, setSubmitAttempted] = useState(false);
  const [snackbar, setSnackbar] = useState("");
  const [draft, setDraft] = useState<SeasonalCardSubmissionDraft>({
    storeCode: "",
    seasonYear: String(new Date().getFullYear()),
    cardType: "",
    catalogGuid: "",
    remainingQuantity: "",
    customUnitPrice: "",
    remark: "",
  });
  const [historyFilters, setHistoryFilters] = useState({
    storeCode: "",
    seasonYear: String(new Date().getFullYear()),
    cardType: "",
  });
  const deviceBoundStoreCode = getDeviceBoundStoreCode({ isDeviceMode, selectedStoreCode });
  const getCardTypeLabel = (cardType: SeasonalCardType | null, cardTypeName?: string | null) => {
    return getSeasonalCardLocalizedTypeLabel(cardType, cardTypeName, (key) => t(key));
  };

  const catalogQuery = useSeasonalCardCatalog(shouldEnableSeasonalCardCatalog(canSubmit));
  const catalogGroups = useMemo(
    () => getSeasonalCardCatalogGroups(catalogQuery.data ?? []),
    [catalogQuery.data]
  );
  const formCardTypeItems = useMemo<SelectionListItem[]>(
    () =>
      getSeasonalCardHistoryCardTypeOptions().map((item) => ({
        ...item,
        label: t(`cardTypes.${item.key}`),
      })),
    [t]
  );
  const historyCardTypeItems = useMemo<SelectionListItem[]>(
    () =>
      getSeasonalCardHistoryCardTypeOptions().map((item) => ({
        ...item,
        label: t(`cardTypes.${item.key}`),
      })),
    [t]
  );
  const selectedFormStore = useMemo(
    () => stores.find((store) => store.storeCode === draft.storeCode) ?? null,
    [draft.storeCode, stores]
  );
  const selectedHistoryStore = useMemo(
    () => stores.find((store) => store.storeCode === historyFilters.storeCode) ?? null,
    [historyFilters.storeCode, stores]
  );
  const selectedCatalogGroup = useMemo(
    () =>
      catalogGroups.find((group) => String(group.cardType) === draft.cardType) ?? null,
    [catalogGroups, draft.cardType]
  );
  const selectedFormCardTypeLabel = draft.cardType
    ? getCardTypeLabel(Number(draft.cardType) as SeasonalCardType)
    : "";
  const selectedCatalogOption = useMemo<SeasonalCardCatalogItem | null>(
    () =>
      selectedCatalogGroup?.options.find(
        (option) => option.catalogGuid === draft.catalogGuid
      ) ?? null,
    [draft.catalogGuid, selectedCatalogGroup]
  );
  const priceOptionItems = useMemo<SelectionListItem[]>(
    () =>
      (selectedCatalogGroup?.options ?? []).map((option) => ({
        key: option.catalogGuid,
        label: formatSeasonalCardPriceOptionLabel(
          option,
          t("form.customPriceOption")
        ),
        description:
          option.fixedUnitPrice != null && !isSeasonalCardCustomPriceOption(option)
            ? formatMoney(option.fixedUnitPrice)
            : null,
      })),
    [selectedCatalogGroup?.options, t]
  );
  const historyQuery = useSeasonalCardSubmissions(
    {
      storeCode: historyFilters.storeCode,
      cardType: historyFilters.cardType ? Number(historyFilters.cardType) : undefined,
      seasonYear: historyFilters.seasonYear,
      pageNumber: historyPageNumber,
      pageSize: PAGE_SIZE,
    },
    canView && Boolean(historyFilters.storeCode) && (!isDeviceMode || Boolean(deviceBoundStoreCode))
  );
  const submitMutation = useSubmitSeasonalCardSubmission();
  const detailQuery = useSeasonalCardSubmissionDetail(
    selectedSubmissionGuid,
    Boolean(selectedSubmissionGuid)
  );
  const formErrors = useMemo(
    () => validateSeasonalCardSubmissionDraft(draft, selectedCatalogOption),
    [draft, selectedCatalogOption]
  );
  const pageCount = getPageCount(
    historyQuery.data?.total ?? 0,
    historyQuery.data?.pageSize ?? PAGE_SIZE
  );
  const historyTable = useMemo(
    () => buildHistoryTable(historyQuery.data?.items ?? []),
    [historyQuery.data?.items]
  );

  useEffect(() => {
    if (!canSubmit && canView) {
      setViewMode("history");
    }
  }, [canSubmit, canView]);

  useEffect(() => {
    if (deviceBoundStoreCode) {
      setDraft((current) =>
        current.storeCode === deviceBoundStoreCode
          ? current
          : {
              ...current,
              storeCode: deviceBoundStoreCode,
            }
      );
      setHistoryFilters((current) =>
        current.storeCode === deviceBoundStoreCode
          ? current
          : {
              ...current,
              storeCode: deviceBoundStoreCode,
            }
      );
      return;
    }

    if (isDeviceMode) {
      setDraft((current) =>
        current.storeCode
          ? {
              ...current,
              storeCode: "",
            }
          : current
      );
      setHistoryFilters((current) =>
        current.storeCode
          ? {
              ...current,
              storeCode: "",
            }
          : current
      );
      return;
    }

    if (!selectedStore?.storeCode) {
      return;
    }

    setDraft((current) =>
      current.storeCode
        ? current
        : {
            ...current,
            storeCode: selectedStore.storeCode,
          }
    );
    setHistoryFilters((current) =>
      current.storeCode
        ? current
        : {
            ...current,
            storeCode: selectedStore.storeCode,
          }
    );
  }, [deviceBoundStoreCode, isDeviceMode, selectedStore?.storeCode]);

  useEffect(() => {
    if (!selectedCatalogGroup?.options.length) {
      return;
    }

    if (
      draft.catalogGuid &&
      selectedCatalogGroup.options.some(
        (option) => option.catalogGuid === draft.catalogGuid
      )
    ) {
      return;
    }

    setDraft((current) => ({
      ...current,
      catalogGuid: selectedCatalogGroup.options[0]?.catalogGuid ?? "",
      customUnitPrice: "",
    }));
  }, [draft.catalogGuid, selectedCatalogGroup]);

  const currentStorePickerSelection =
    storePickerTarget === "formStore"
      ? draft.storeCode
      : storePickerTarget === "historyStore"
        ? historyFilters.storeCode
        : null;
  const currentYearPickerSelection =
    yearPickerTarget === "formYear"
      ? draft.seasonYear
      : yearPickerTarget === "historyYear"
        ? historyFilters.seasonYear
        : null;
  const currentCardTypePickerSelection =
    cardTypePickerTarget === "formCardType"
      ? draft.cardType
      : cardTypePickerTarget === "historyCardType"
        ? historyFilters.cardType
        : null;

  const handleSelectStore = (store: Store | null) => {
    if (storePickerTarget === "formStore") {
      setDraft((current) => ({
        ...current,
        storeCode: deviceBoundStoreCode ?? store?.storeCode ?? "",
      }));
    }

    if (storePickerTarget === "historyStore") {
      setHistoryPageNumber(1);
      setHistoryFilters((current) => ({
        ...current,
        storeCode: deviceBoundStoreCode ?? store?.storeCode ?? "",
      }));
    }

    setStorePickerTarget(null);
  };

  const handleSelectYear = (item: SelectionListItem | null) => {
    if (yearPickerTarget === "formYear") {
      const value = item?.key ?? String(new Date().getFullYear());
      setDraft((current) => ({ ...current, seasonYear: value }));
    }

    if (yearPickerTarget === "historyYear") {
      setHistoryPageNumber(1);
      setHistoryFilters((current) => ({ ...current, seasonYear: item?.key ?? "" }));
    }

    setYearPickerTarget(null);
  };

  const handleSelectCardType = (item: SelectionListItem | null) => {
    if (cardTypePickerTarget === "formCardType") {
      setDraft((current) => ({
        ...current,
        cardType: item?.key ?? "",
        catalogGuid: "",
        customUnitPrice: "",
      }));
    }

    if (cardTypePickerTarget === "historyCardType") {
      setHistoryPageNumber(1);
      setHistoryFilters((current) => ({
        ...current,
        cardType: item?.key ?? "",
      }));
    }

    setCardTypePickerTarget(null);
  };

  const handleSelectPriceOption = (item: SelectionListItem | null) => {
    setDraft((current) => ({
      ...current,
      catalogGuid: item?.key ?? "",
      customUnitPrice: "",
    }));
    setPriceOptionPickerVisible(false);
  };

  const refreshCurrentView = async () => {
    if (viewMode === "history" && canView) {
      await historyQuery.refetch();
      return;
    }

      if (shouldEnableSeasonalCardCatalog(canSubmit)) {
        await catalogQuery.refetch();
      }
  };

  const handleSubmit = async () => {
    const submitStoreCode = deviceBoundStoreCode ?? draft.storeCode;
    if (isDeviceMode && !submitStoreCode) {
      setSnackbar(t("messages.formFixErrors"));
      return;
    }

    setSubmitAttempted(true);
    if (Object.keys(formErrors).length > 0) {
      setSnackbar(t("messages.formFixErrors"));
      return;
    }

    try {
      await submitMutation.mutateAsync({
        storeCode: submitStoreCode,
        catalogGuid: draft.catalogGuid,
        seasonYear: draft.seasonYear,
        remainingQuantity: draft.remainingQuantity,
        customUnitPrice: isSeasonalCardCustomPriceOption(selectedCatalogOption)
          ? draft.customUnitPrice
          : undefined,
        remark: draft.remark,
      });
      setSnackbar(t("messages.submitSuccess"));
      setDraft((current) => ({
        ...current,
        remainingQuantity: "",
        customUnitPrice: "",
        remark: "",
      }));
      setHistoryPageNumber(1);
      setHistoryFilters((current) => ({
        ...current,
        storeCode: draft.storeCode,
        seasonYear: draft.seasonYear,
        cardType: draft.cardType,
      }));
      if (canView) {
        setViewMode("history");
      }
    } catch (error) {
      setSnackbar(error instanceof Error ? error.message : t("messages.submitFailed"));
    }
  };

  if (!canView && !canSubmit) {
    return (
      <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
        <EmptyState
          title={t("messages.noAccessTitle")}
          description={t("messages.noAccessDescription")}
        />
      </SafeAreaView>
    );
  }

  const renderSubmitView = () => (
    <View style={styles.section}>
      <Surface style={styles.headerCard}>
        <Text variant="titleMedium">{t("title")}</Text>
        <Text variant="bodyMedium" style={styles.subtitle}>
          {t("submitSubtitle")}
        </Text>
      </Surface>

      <FieldButton
        label={t("form.storeCode")}
        value={selectedFormStore?.storeName ?? draft.storeCode}
        placeholder={t("form.selectStore")}
        onPress={() => setStorePickerTarget("formStore")}
      />
      {submitAttempted && formErrors.storeCode ? (
        <HelperText type="error" visible>
          {t("errors.storeCode")}
        </HelperText>
      ) : null}

      <View style={styles.compactRow}>
        <FieldButton
          label={t("form.seasonYear")}
          value={draft.seasonYear}
          placeholder={t("form.selectSeasonYear")}
          onPress={() => setYearPickerTarget("formYear")}
          style={styles.compactField}
        />
        <FieldButton
          label={t("form.cardType")}
          value={selectedFormCardTypeLabel}
          placeholder={t("form.selectCardType")}
          onPress={() => setCardTypePickerTarget("formCardType")}
          style={styles.compactField}
        />
      </View>
      {submitAttempted && formErrors.seasonYear ? (
        <HelperText type="error" visible>
          {t("errors.seasonYear")}
        </HelperText>
      ) : null}

      <View style={styles.compactRow}>
        <FieldButton
          label={t("form.priceOption")}
          value={
            selectedCatalogOption
              ? formatSeasonalCardPriceOptionLabel(
                  selectedCatalogOption,
                  t("form.customPriceOption")
                )
              : ""
          }
          placeholder={t("form.selectPriceOption")}
          onPress={() => setPriceOptionPickerVisible(true)}
          style={styles.compactField}
        />
        <TextInput
          dense
          label={t("form.remainingQuantity")}
          mode="outlined"
          keyboardType="number-pad"
          value={draft.remainingQuantity}
          style={[styles.compactField, styles.compactInput]}
          onChangeText={(value) =>
            setDraft((current) => ({ ...current, remainingQuantity: value }))
          }
        />
      </View>
      {submitAttempted && formErrors.catalogGuid ? (
        <HelperText type="error" visible>
          {t("errors.catalogGuid")}
        </HelperText>
      ) : null}
      {submitAttempted && formErrors.remainingQuantity ? (
        <HelperText type="error" visible>
          {t("errors.remainingQuantity")}
        </HelperText>
      ) : null}

      {isSeasonalCardCustomPriceOption(selectedCatalogOption) ? (
        <>
          <TextInput
            dense
            label={t("form.customUnitPrice")}
            mode="outlined"
            keyboardType="decimal-pad"
            value={draft.customUnitPrice}
            style={styles.compactInput}
            onChangeText={(value) =>
              setDraft((current) => ({ ...current, customUnitPrice: value }))
            }
          />
          {submitAttempted && formErrors.customUnitPrice ? (
            <HelperText type="error" visible>
              {t("errors.customUnitPrice")}
            </HelperText>
          ) : null}
        </>
      ) : null}

      <TextInput
        dense
        label={t("form.remark")}
        mode="outlined"
        multiline
        numberOfLines={2}
        value={draft.remark}
        style={styles.compactRemark}
        onChangeText={(value) => setDraft((current) => ({ ...current, remark: value }))}
      />

      <Button
        mode="contained"
        compact
        loading={submitMutation.isPending}
        disabled={
          submitMutation.isPending ||
          catalogQuery.isLoading ||
          !selectedCatalogOption
        }
        contentStyle={styles.submitButtonContent}
        onPress={() => void handleSubmit()}
      >
        {t("common:actions.submit")}
      </Button>
    </View>
  );

  const renderHistoryView = () => (
    <View style={styles.section}>
      <Surface style={styles.headerCard}>
        <Text variant="titleMedium">{t("historyTitle")}</Text>
        <Text variant="bodyMedium" style={styles.subtitle}>
          {t("historySubtitle")}
        </Text>
      </Surface>

      <FieldButton
        label={t("history.storeCode")}
        value={selectedHistoryStore?.storeName ?? historyFilters.storeCode}
        placeholder={t("form.selectStore")}
        onPress={() => setStorePickerTarget("historyStore")}
      />
      <View style={styles.compactRow}>
        <FieldButton
          label={t("history.seasonYear")}
          value={historyFilters.seasonYear || t("history.allYears")}
          placeholder={t("form.selectSeasonYear")}
          onPress={() => setYearPickerTarget("historyYear")}
          style={styles.compactField}
        />
        <FieldButton
          label={t("history.cardType")}
          value={
            historyFilters.cardType
              ? getCardTypeLabel(Number(historyFilters.cardType) as SeasonalCardType)
              : ""
          }
          placeholder={t("history.allCardTypes")}
          onPress={() => setCardTypePickerTarget("historyCardType")}
          style={styles.compactField}
        />
      </View>

      <SegmentedButtons
        value={historyDisplayMode}
        onValueChange={(value) => setHistoryDisplayMode(value as HistoryDisplayMode)}
        buttons={[
          { value: "list", label: t("history.displayList") },
          { value: "table", label: t("history.displayTable") },
        ]}
        style={styles.historyDisplaySwitch}
      />

      {historyQuery.isLoading ? (
        <View style={styles.feedbackBlock}>
          <ActivityIndicator />
        </View>
      ) : historyQuery.isError ? (
        <EmptyState
          title={t("messages.historyLoadFailed")}
          description={
            historyQuery.error instanceof Error
              ? historyQuery.error.message
              : t("messages.historyLoadFailed")
          }
        />
      ) : historyQuery.data?.items.length && historyDisplayMode === "table" ? (
        <Surface style={styles.tableSurface}>
          <ScrollView horizontal showsHorizontalScrollIndicator>
            <DataTable style={styles.historyTable}>
              <DataTable.Header>
                <DataTable.Title style={styles.priceHeaderCell}>
                  {t("history.priceTypeHeader")}
                </DataTable.Title>
                {historyTable.years.map((year) => (
                  <DataTable.Title key={year} numeric style={styles.yearHeaderCell}>
                    {String(year)}
                  </DataTable.Title>
                ))}
              </DataTable.Header>
              {historyTable.rows.map((row) => (
                <DataTable.Row key={row.priceLabel}>
                  <DataTable.Cell style={styles.priceHeaderCell}>
                    {row.priceLabel}
                  </DataTable.Cell>
                  {historyTable.years.map((year) => (
                    <DataTable.Cell key={year} numeric style={styles.yearHeaderCell}>
                      {row.cells.get(year)?.quantity ?? "--"}
                    </DataTable.Cell>
                  ))}
                </DataTable.Row>
              ))}
            </DataTable>
          </ScrollView>
        </Surface>
      ) : historyQuery.data?.items.length ? (
        <View style={styles.cardList}>
          {historyQuery.data.items.map((item) => (
            <Card key={item.submissionGuid} mode="outlined">
              <Card.Content style={styles.cardContent}>
                <Text variant="titleMedium">
                  {getCardTypeLabel(item.cardType, item.cardTypeName) ||
                    t("common:none")}
                </Text>
                <DetailLine label={t("labels.storeCode")} value={item.storeCode || "--"} />
                <DetailLine
                  label={t("labels.seasonYear")}
                  value={item.seasonYear != null ? String(item.seasonYear) : "--"}
                />
                <DetailLine
                  label={t("labels.unitPrice")}
                  value={item.priceLabel || formatMoney(item.unitPrice)}
                />
                <DetailLine
                  label={t("labels.remainingQuantity")}
                  value={
                    item.remainingQuantity != null
                      ? String(item.remainingQuantity)
                      : "--"
                  }
                />
                <DetailLine
                  label={t("labels.submittedByName")}
                  value={item.submittedByName || "--"}
                />
                <DetailLine
                  label={t("labels.submittedAt")}
                  value={formatDateTime(item.submittedAt, localeTag)}
                />
                <DetailLine label={t("labels.remark")} value={item.remark || "--"} />
                <Button
                  compact
                  mode="text"
                  onPress={() => setSelectedSubmissionGuid(item.submissionGuid)}
                >
                  {t("common:actions.viewDetail")}
                </Button>
              </Card.Content>
            </Card>
          ))}
        </View>
      ) : (
        <EmptyState
          title={t("messages.emptyHistory")}
          description={t("messages.emptyHistoryDescription")}
        />
      )}

      {historyQuery.data?.items.length ? (
        <View style={styles.pagination}>
          <Button
            compact
            mode="outlined"
            disabled={historyPageNumber <= 1}
            onPress={() =>
              setHistoryPageNumber((current) => Math.max(1, current - 1))
            }
          >
            {t("common:actions.back")}
          </Button>
          <Text variant="bodyMedium">
            {historyPageNumber} / {pageCount}
          </Text>
          <Button
            compact
            mode="outlined"
            disabled={historyPageNumber >= pageCount}
            onPress={() =>
              setHistoryPageNumber((current) =>
                Math.min(pageCount, current + 1)
              )
            }
          >
            {t("history.nextPage")}
          </Button>
        </View>
      ) : null}
    </View>
  );

  const yearItems = yearOptions.map((year) => ({
    key: String(year),
    label: String(year),
  }));

  return (
    <SafeAreaView style={styles.screen} edges={["top", "left", "right"]}>
      <ScrollView
        contentInsetAdjustmentBehavior="automatic"
        refreshControl={
          <RefreshControl
            refreshing={Boolean(
              viewMode === "history" ? historyQuery.isRefetching : catalogQuery.isRefetching
            )}
            onRefresh={() => {
              void refreshCurrentView();
            }}
          />
        }
        contentContainerStyle={styles.content}
      >
        {canSubmit && canView ? (
          <SegmentedButtons
            value={viewMode}
            onValueChange={(value) => setViewMode(value as ViewMode)}
            buttons={[
              { value: "submit", label: t("viewModes.submit") },
              { value: "history", label: t("viewModes.history") },
            ]}
          />
        ) : null}

        {viewMode === "submit" ? renderSubmitView() : renderHistoryView()}
      </ScrollView>

      <StorePickerModal
        visible={Boolean(storePickerTarget)}
        stores={stores}
        selectedStoreCode={currentStorePickerSelection}
        title={t("form.storePickerTitle")}
        cancelLabel={t("common:actions.cancel")}
        onDismiss={() => setStorePickerTarget(null)}
        onSelectStore={(store) => {
          handleSelectStore(store);
        }}
      />

      <SelectionListModal
        visible={Boolean(yearPickerTarget)}
        title={t("form.yearPickerTitle")}
        cancelLabel={t("common:actions.cancel")}
        items={yearItems}
        selectedKey={currentYearPickerSelection}
        includeAllOption={yearPickerTarget === "historyYear"}
        allLabel={t("history.allYears")}
        emptyLabel={t("messages.emptyOptions")}
        onDismiss={() => setYearPickerTarget(null)}
        onSelect={(item) => {
          handleSelectYear(item);
        }}
      />

      <SelectionListModal
        visible={Boolean(cardTypePickerTarget)}
        title={t("form.cardTypePickerTitle")}
        cancelLabel={t("common:actions.cancel")}
        items={
          cardTypePickerTarget === "formCardType"
            ? formCardTypeItems
            : historyCardTypeItems
        }
        selectedKey={currentCardTypePickerSelection}
        includeAllOption={cardTypePickerTarget === "historyCardType"}
        allLabel={t("history.allCardTypes")}
        emptyLabel={
          cardTypePickerTarget === "formCardType"
            ? t("messages.emptyOptions")
            : t("messages.emptyOptions")
        }
        loading={false}
        onDismiss={() => setCardTypePickerTarget(null)}
        onSelect={(item) => {
          handleSelectCardType(item);
        }}
      />

      <SelectionListModal
        visible={priceOptionPickerVisible}
        title={t("form.priceOptionPickerTitle")}
        cancelLabel={t("common:actions.cancel")}
        items={priceOptionItems}
        selectedKey={draft.catalogGuid}
        emptyLabel={
          draft.cardType ? t("messages.catalogEmpty") : t("messages.selectCardTypeFirst")
        }
        loading={catalogQuery.isLoading}
        onDismiss={() => setPriceOptionPickerVisible(false)}
        onSelect={(item) => {
          handleSelectPriceOption(item);
        }}
      />

      <Portal>
        <Modal
          visible={Boolean(selectedSubmissionGuid)}
          onDismiss={() => setSelectedSubmissionGuid(null)}
          contentContainerStyle={styles.detailModal}
        >
          <Text variant="titleLarge" style={styles.detailTitle}>
            {t("detailTitle")}
          </Text>
          {detailQuery.isLoading ? (
            <View style={styles.feedbackBlock}>
              <ActivityIndicator />
            </View>
          ) : detailQuery.data ? (
            <View style={styles.detailBlock}>
              <DetailLine
                label={t("labels.storeCode")}
                value={detailQuery.data.storeCode || "--"}
              />
              <DetailLine
                label={t("labels.cardType")}
                value={
                  getCardTypeLabel(
                    detailQuery.data.cardType,
                    detailQuery.data.cardTypeName
                  ) || "--"
                }
              />
              <DetailLine
                label={t("labels.seasonYear")}
                value={
                  detailQuery.data.seasonYear != null
                    ? String(detailQuery.data.seasonYear)
                    : "--"
                }
              />
              <DetailLine
                label={t("labels.unitPrice")}
                value={detailQuery.data.priceLabel || formatMoney(detailQuery.data.unitPrice)}
              />
              <DetailLine
                label={t("labels.remainingQuantity")}
                value={
                  detailQuery.data.remainingQuantity != null
                    ? String(detailQuery.data.remainingQuantity)
                    : "--"
                }
              />
              <DetailLine
                label={t("labels.submittedByName")}
                value={detailQuery.data.submittedByName || "--"}
              />
              <DetailLine
                label={t("labels.submittedAt")}
                value={formatDateTime(detailQuery.data.submittedAt, localeTag)}
              />
              <DetailLine
                label={t("labels.remark")}
                value={detailQuery.data.remark || "--"}
              />
            </View>
          ) : (
            <EmptyState
              title={t("messages.detailLoadFailed")}
              description={t("messages.detailLoadFailedDescription")}
            />
          )}
          <View style={styles.modalActions}>
            <Button onPress={() => setSelectedSubmissionGuid(null)}>
              {t("common:actions.close")}
            </Button>
          </View>
        </Modal>
      </Portal>

      <Snackbar visible={Boolean(snackbar)} onDismiss={() => setSnackbar("")}>
        {snackbar}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  cardContent: {
    gap: 4,
  },
  cardList: {
    gap: 8,
  },
  compactField: {
    flex: 1,
  },
  compactInput: {
    backgroundColor: "#FFFFFF",
    minHeight: 48,
  },
  compactRemark: {
    backgroundColor: "#FFFFFF",
    minHeight: 58,
  },
  compactRow: {
    flexDirection: "row",
    gap: 8,
  },
  content: {
    gap: 10,
    padding: 10,
    paddingBottom: 16,
  },
  detailBlock: {
    gap: 6,
  },
  detailLine: {
    gap: 1,
  },
  detailLineLabel: {
    color: "#666",
  },
  detailLineValue: {
    color: "#111",
  },
  detailModal: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 16,
    gap: 12,
    padding: 20,
    width: "88%",
  },
  detailTitle: {
    marginBottom: 4,
  },
  feedbackBlock: {
    alignItems: "center",
    justifyContent: "center",
    minHeight: 120,
  },
  fieldButtonContent: {
    justifyContent: "flex-start",
    minHeight: 34,
    paddingHorizontal: 2,
  },
  fieldButtonLabel: {
    fontSize: 13,
    marginVertical: 2,
  },
  fieldLabel: {
    color: "#555",
    fontSize: 12,
    lineHeight: 15,
  },
  fieldSurface: {
    borderRadius: 12,
    gap: 4,
    padding: 8,
  },
  headerCard: {
    borderRadius: 12,
    gap: 2,
    padding: 10,
  },
  historyDisplaySwitch: {
    marginTop: 2,
  },
  historyTable: {
    minWidth: 320,
  },
  modalActions: {
    alignItems: "flex-end",
    marginTop: 8,
  },
  pagination: {
    alignItems: "center",
    flexDirection: "row",
    gap: 12,
    justifyContent: "space-between",
  },
  priceHeaderCell: {
    minWidth: 96,
  },
  screen: {
    backgroundColor: "#F7F8FA",
    flex: 1,
  },
  section: {
    gap: 6,
  },
  submitButtonContent: {
    minHeight: 40,
  },
  tableSurface: {
    borderRadius: 12,
    overflow: "hidden",
  },
  subtitle: {
    color: "#666",
    fontSize: 12,
    lineHeight: 16,
  },
  yearHeaderCell: {
    minWidth: 72,
  },
});
