import { useMemo, useState } from "react";
import { Alert, Image, RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActivityIndicator,
  Button,
  Card,
  Checkbox,
  Chip,
  Divider,
  Menu,
  Modal,
  Portal,
  Snackbar,
  Surface,
  Switch,
  Text,
  TextInput,
} from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { EmptyState } from "@/components/ui/EmptyState";
import { useAuthStore } from "@/store/auth-store";
import {
  alignDomesticProductCode,
  applyFloatRate,
  applyPrices,
  backfill,
  batchDeleteDetails,
  batchUpdateDetails,
  createProductCreationJob,
  createPushProductsToHqJob,
  createSubmitJob,
  exportContainerDetails,
  getContainerDetail,
  queryContainerProducts,
  recalculate,
  wait,
  waitPushProductsToHqJob,
  waitSubmitJob,
} from "./api";
import {
  DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMNS,
  DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMNS,
  buildBatchScope,
  buildContainerDetailQuery,
  buildCreateProductsOperationId,
  buildPushProductsToHqOperationId,
  buildSubmitContainerOperationId,
  getCurrentPageDetailGuids,
  getDetailBarcode,
  getDetailEnglishName,
  getDetailGuid,
  getDetailImageUrl,
  getDetailItemNumber,
  getDetailDomesticProductCode,
  getDetailLocalProductCode,
  getDetailLocalSupplierCode,
  getDetailMatchType,
  getDetailProductName,
  getDetailReadonlyOemPrice,
  getDetailRealtimeImportPrice,
  getDetailRealtimeRetailPrice,
  getDetailVisibleOemPrice,
  hasDetailProductCodeConflict,
  toPushProductsToHqItems,
  toggleCurrentPageSelection,
  toggleSelectedTag,
  trimToUndefined,
} from "./query";
import type {
  ContainerDetail,
  ContainerDetailQuery,
  ContainerDetailQueryTag,
  ContainerExportFormat,
  UpdateContainerDetailRequest,
} from "./types";

const TAGS: Array<{ value: ContainerDetailQueryTag; label: string }> = [
  { value: "all", label: "全部" },
  { value: "new", label: "新商品" },
  { value: "existing", label: "已有" },
  { value: "noOemPrice", label: "缺零售价" },
  { value: "abnormalImport", label: "进口异常" },
  { value: "active", label: "启用" },
  { value: "inactive", label: "停用" },
];

type BulkModalType = "float" | "prices" | null;

interface EditForm {
  productName: string;
  englishName: string;
  domesticPrice: string;
  importPrice: string;
  oemPrice: string;
  floatRate: string;
  containerQuantity: string;
  middlePackQuantity: string;
  isActive: boolean;
}

interface DetailRangeFilterForm {
  containerQuantityMin: string;
  containerQuantityMax: string;
  middlePackQuantityMin: string;
  middlePackQuantityMax: string;
  warehouseImportPriceMin: string;
  warehouseImportPriceMax: string;
  oemPriceMin: string;
  oemPriceMax: string;
}

const EMPTY_DETAIL_RANGE_FILTERS: DetailRangeFilterForm = {
  containerQuantityMin: "",
  containerQuantityMax: "",
  middlePackQuantityMin: "",
  middlePackQuantityMax: "",
  warehouseImportPriceMin: "",
  warehouseImportPriceMax: "",
  oemPriceMin: "",
  oemPriceMax: "",
};

const DETAIL_RANGE_FILTER_KEYS = Object.keys(EMPTY_DETAIL_RANGE_FILTERS) as Array<keyof DetailRangeFilterForm>;

function formatDate(value?: string) {
  return value ? value.slice(0, 10) : "--";
}

function formatNumber(value?: number | null, digits = 2) {
  return value == null || !Number.isFinite(value) ? "--" : value.toFixed(digits);
}

function numberInput(value?: number | null) {
  return value == null || !Number.isFinite(value) ? "" : String(value);
}

function parseOptionalNumber(value: string) {
  const trimmed = value.trim();
  if (!trimmed) return undefined;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : Number.NaN;
}

function getMatchTypeLabel(value: ReturnType<typeof getDetailMatchType>) {
  if (value === "productCode") return "商品编码";
  if (value === "supplierItem") return "供应商货号";
  return "未匹配";
}

function hasRangeFilters(filters: DetailRangeFilterForm) {
  return DETAIL_RANGE_FILTER_KEYS.some((key) => Boolean(filters[key].trim()));
}

function hasInvalidRangeFilters(filters: DetailRangeFilterForm) {
  return DETAIL_RANGE_FILTER_KEYS.some((key) => Number.isNaN(parseOptionalNumber(filters[key])));
}

function buildRangeQuery(filters: DetailRangeFilterForm): Partial<ContainerDetailQuery> {
  return {
    containerQuantityMin: parseOptionalNumber(filters.containerQuantityMin),
    containerQuantityMax: parseOptionalNumber(filters.containerQuantityMax),
    middlePackQuantityMin: parseOptionalNumber(filters.middlePackQuantityMin),
    middlePackQuantityMax: parseOptionalNumber(filters.middlePackQuantityMax),
    warehouseImportPriceMin: parseOptionalNumber(filters.warehouseImportPriceMin),
    warehouseImportPriceMax: parseOptionalNumber(filters.warehouseImportPriceMax),
    oemPriceMin: parseOptionalNumber(filters.oemPriceMin),
    oemPriceMax: parseOptionalNumber(filters.oemPriceMax),
  };
}

function buildEditForm(detail: ContainerDetail): EditForm {
  return {
    productName: getDetailProductName(detail),
    englishName: getDetailEnglishName(detail),
    domesticPrice: numberInput(detail.国内价格),
    importPrice: numberInput(detail.进口价格),
    oemPrice: numberInput(getDetailVisibleOemPrice(detail)),
    floatRate: numberInput(detail.调整浮率),
    containerQuantity: numberInput(detail.装柜数量),
    middlePackQuantity: numberInput(detail.中包数),
    isActive: detail.IsActive ?? detail.warehouseIsActive ?? true,
  };
}

function buildEditPayload(detail: ContainerDetail, form: EditForm): UpdateContainerDetailRequest {
  const hguid = getDetailGuid(detail);
  const payload = {
    hguid,
    商品名称: trimToUndefined(form.productName),
    英文名称: trimToUndefined(form.englishName),
    国内价格: parseOptionalNumber(form.domesticPrice),
    进口价格: parseOptionalNumber(form.importPrice),
    贴牌价格: parseOptionalNumber(form.oemPrice),
    调整浮率: parseOptionalNumber(form.floatRate),
    装柜数量: parseOptionalNumber(form.containerQuantity),
    中包数: parseOptionalNumber(form.middlePackQuantity),
    IsActive: form.isActive,
  };
  const invalid = Object.values(payload).some((value) => Number.isNaN(value));
  if (invalid) {
    throw new Error("编辑字段存在无效数字");
  }
  return payload;
}

function DetailMetric({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.metric}>
      <Text variant="labelSmall" style={styles.muted}>{label}</Text>
      <Text variant="bodyMedium">{value}</Text>
    </View>
  );
}

function DetailCard({
  detail,
  selected,
  canEditContainer,
  canAlignDomesticProductCode,
  showReadonlyOemPrice,
  aligning,
  alignDisabled,
  onEdit,
  onAlign,
  onToggle,
}: {
  detail: ContainerDetail;
  selected: boolean;
  canEditContainer: boolean;
  canAlignDomesticProductCode: boolean;
  showReadonlyOemPrice: boolean;
  aligning: boolean;
  alignDisabled: boolean;
  onEdit: () => void;
  onAlign: () => void;
  onToggle: () => void;
}) {
  const imageUrl = getDetailImageUrl(detail);
  const [imageFailed, setImageFailed] = useState(false);
  const showImage = Boolean(imageUrl && !imageFailed);
  const localProductCode = getDetailLocalProductCode(detail);
  const domesticProductCode = getDetailDomesticProductCode(detail);
  const hasConflict = hasDetailProductCodeConflict(detail);
  const matchType = getDetailMatchType(detail);
  const isSetChild = detail.商品类型 === "套装子商品" || detail.商品信息?.商品类型 === "套装子商品";
  const canAlign = canAlignDomesticProductCode && hasConflict && Boolean(localProductCode && domesticProductCode) && !isSetChild;

  return (
    <Card style={styles.card} mode="outlined">
      <Card.Title
        title={getDetailItemNumber(detail) || detail.商品编码 || "未命名商品"}
        subtitle={getDetailProductName(detail) || getDetailEnglishName(detail) || "--"}
        titleNumberOfLines={1}
        subtitleNumberOfLines={1}
        left={() => (
          <Checkbox.Android
            status={selected ? "checked" : "unchecked"}
            onPress={onToggle}
          />
        )}
        right={() => (
          <View style={styles.detailImageFrame}>
            {showImage ? (
              <Image
                source={{ uri: imageUrl! }}
                style={styles.detailImage}
                resizeMode="contain"
                onError={() => setImageFailed(true)}
              />
            ) : (
              <View style={styles.detailImagePlaceholder}>
                <Text variant="labelSmall" style={styles.imagePlaceholderText}>图片</Text>
              </View>
            )}
          </View>
        )}
      />
      <Card.Content>
        <View style={styles.chipRow}>
          <Chip compact>{detail.是否新商品 ? "新商品" : "已有商品"}</Chip>
          <Chip compact>{detail.warehouseIsActive === false ? "停用" : "启用"}</Chip>
          {detail.matchType || detail.MatchType || hasConflict ? <Chip compact>{getMatchTypeLabel(matchType)}</Chip> : null}
        </View>
        <View style={styles.metricGrid}>
          <DetailMetric label="条码" value={getDetailBarcode(detail) || "--"} />
          <DetailMetric label="数量" value={formatNumber(detail.装柜数量, 0)} />
          <DetailMetric label="中包" value={formatNumber(detail.中包数, 0)} />
          <DetailMetric label="国内价" value={formatNumber(detail.国内价格)} />
          <DetailMetric label="实时进货价" value={formatNumber(getDetailRealtimeImportPrice(detail))} />
          <DetailMetric label="进口价" value={formatNumber(detail.进口价格)} />
          <DetailMetric label="零售价" value={formatNumber(getDetailVisibleOemPrice(detail))} />
          <DetailMetric label="实时零售价" value={formatNumber(getDetailRealtimeRetailPrice(detail))} />
          {showReadonlyOemPrice ? <DetailMetric label="只读零售价" value={formatNumber(getDetailReadonlyOemPrice(detail))} /> : null}
        </View>
        {hasConflict ? (
          <Text style={styles.warningText}>
            编码冲突：国内 {domesticProductCode || "--"} / 本地 {localProductCode || "--"}
          </Text>
        ) : null}
        {detail.备注 ? <Text style={styles.remark}>{detail.备注}</Text> : null}
      </Card.Content>
      {canEditContainer || canAlign ? (
        <Card.Actions>
          {canEditContainer ? <Button icon="pencil" onPress={onEdit}>编辑明细</Button> : null}
          {canAlign ? (
            <Button icon="link-variant" loading={aligning} disabled={alignDisabled} onPress={onAlign}>
              对齐编码
            </Button>
          ) : null}
        </Card.Actions>
      ) : null}
    </Card>
  );
}

export function ContainerDetailScreen({ containerGuid }: { containerGuid: string }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const access = useAuthStore((state) => state.access);
  const [keyword, setKeyword] = useState("");
  const [appliedKeyword, setAppliedKeyword] = useState("");
  const [selectedTags, setSelectedTags] = useState<ContainerDetailQueryTag[]>([]);
  const [page, setPage] = useState(1);
  const [selectedHguids, setSelectedHguids] = useState<string[]>([]);
  const [bulkMenuVisible, setBulkMenuVisible] = useState(false);
  const [bulkModalType, setBulkModalType] = useState<BulkModalType>(null);
  const [bulkFloatRate, setBulkFloatRate] = useState("");
  const [bulkImportPrice, setBulkImportPrice] = useState("");
  const [bulkOemPrice, setBulkOemPrice] = useState("");
  const [editingDetail, setEditingDetail] = useState<ContainerDetail | null>(null);
  const [editForm, setEditForm] = useState<EditForm | null>(null);
  const [showReadonlyOemPrice, setShowReadonlyOemPrice] = useState(false);
  const [showRangeFilters, setShowRangeFilters] = useState(false);
  const [rangeFilters, setRangeFilters] = useState<DetailRangeFilterForm>(EMPTY_DETAIL_RANGE_FILTERS);
  const [appliedRangeFilters, setAppliedRangeFilters] = useState<DetailRangeFilterForm>(EMPTY_DETAIL_RANGE_FILTERS);
  const [aligningDetailHguid, setAligningDetailHguid] = useState("");
  const [snackbar, setSnackbar] = useState("");

  const headerQuery = useQuery({
    queryKey: ["containers", "detail", containerGuid],
    queryFn: () => getContainerDetail(containerGuid),
    enabled: Boolean(containerGuid) && access.canViewContainers,
  });

  const detailQueryPayload = useMemo(
    () => buildContainerDetailQuery(containerGuid, {
      keyword: appliedKeyword,
      selectedTags,
      pageNumber: page,
      ...buildRangeQuery(appliedRangeFilters),
    }),
    [appliedKeyword, appliedRangeFilters, containerGuid, page, selectedTags],
  );

  const productsQuery = useQuery({
    queryKey: ["containers", "detail-products", detailQueryPayload],
    queryFn: () => queryContainerProducts(containerGuid, detailQueryPayload),
    enabled: Boolean(containerGuid) && access.canViewContainers,
  });

  const details = productsQuery.data?.items ?? [];
  const currentPageDetailGuids = useMemo(() => getCurrentPageDetailGuids(details), [details]);
  const selectedSet = useMemo(
    () => new Set(selectedHguids.map((item) => item.trim()).filter(Boolean)),
    [selectedHguids],
  );
  const currentPageSelectedCount = currentPageDetailGuids.filter((hguid) => selectedSet.has(hguid)).length;
  const isCurrentPageFullySelected = currentPageDetailGuids.length > 0
    && currentPageSelectedCount === currentPageDetailGuids.length;
  const selectedDetails = details.filter((detail) => selectedSet.has(getDetailGuid(detail).trim()));
  const filteredItemCount = productsQuery.data?.itemsTotal ?? details.length;
  const totalPages = Math.max(1, Math.ceil((productsQuery.data?.itemsTotal ?? 0) / detailQueryPayload.pageSize));
  const canEditContainer = access.canEditContainer;
  const canDeleteContainer = access.canDeleteContainer;
  const canRunProductJobs = access.canEditContainer && access.hasPermission("PosProducts.Manage");
  const canAlignDomesticProductCode = canEditContainer && (access.isAdmin || access.hasPermission("Products.Edit"));
  const rangeFilterActive = hasRangeFilters(appliedRangeFilters);

  const invalidateDetail = () => {
    void queryClient.invalidateQueries({ queryKey: ["containers", "detail"] });
    void queryClient.invalidateQueries({ queryKey: ["containers", "detail-products"] });
    void queryClient.invalidateQueries({ queryKey: ["containers", "list"] });
  };

  const updateMutation = useMutation({
    mutationFn: () => {
      if (!editingDetail || !editForm) throw new Error("没有可保存的明细");
      return batchUpdateDetails([buildEditPayload(editingDetail, editForm)]);
    },
    onSuccess: () => {
      setEditingDetail(null);
      setEditForm(null);
      invalidateDetail();
      setSnackbar("明细已保存");
    },
    onError: (error) => setSnackbar(error instanceof Error ? error.message : "保存明细失败"),
  });

  const bulkMutation = useMutation({
    mutationFn: async (action: "delete" | "float" | "prices" | "recalculate" | "backfill") => {
      const scope = buildBatchScope(detailQueryPayload, selectedHguids);
      if (action === "delete") {
        if (!selectedHguids.length) throw new Error("请先选择要删除的明细");
        return batchDeleteDetails(selectedHguids);
      }
      if (action === "float") {
        const rate = parseOptionalNumber(bulkFloatRate);
        if (rate === undefined || Number.isNaN(rate)) throw new Error("请输入有效浮率");
        return applyFloatRate(containerGuid, scope, rate);
      }
      if (action === "prices") {
        const importPrice = parseOptionalNumber(bulkImportPrice);
        const oemPrice = parseOptionalNumber(bulkOemPrice);
        if (
          (importPrice === undefined && oemPrice === undefined) ||
          Number.isNaN(importPrice) ||
          Number.isNaN(oemPrice)
        ) {
          throw new Error("请输入有效进口价或零售价");
        }
        return applyPrices(containerGuid, scope, { importPrice, oemPrice });
      }
      if (action === "recalculate") return recalculate(containerGuid, scope);
      return backfill(containerGuid, scope);
    },
    onSuccess: () => {
      setBulkModalType(null);
      setBulkFloatRate("");
      setBulkImportPrice("");
      setBulkOemPrice("");
      setSelectedHguids([]);
      invalidateDetail();
      setSnackbar("批量操作已完成");
    },
    onError: (error) => setSnackbar(error instanceof Error ? error.message : "批量操作失败"),
  });

  const createProductsMutation = useMutation({
    mutationFn: async () => {
      if (!selectedHguids.length) throw new Error("请先选择新商品明细");
      const job = await createProductCreationJob({
        containerGuid,
        detailHguids: selectedHguids,
        operationId: buildCreateProductsOperationId(containerGuid, selectedHguids),
      });
      return wait(job.jobId);
    },
    onSuccess: (job) => {
      invalidateDetail();
      setSnackbar(job.message ?? `新商品任务完成：失败 ${job.result.failedCount}`);
    },
    onError: (error) => setSnackbar(error instanceof Error ? error.message : "创建新商品失败"),
  });

  const submitMutation = useMutation({
    mutationFn: async () => {
      const job = await createSubmitJob({
        containerGuid,
        operationId: buildSubmitContainerOperationId(containerGuid),
      });
      return waitSubmitJob(job.jobId);
    },
    onSuccess: (job) => {
      invalidateDetail();
      setSnackbar(job.message ?? `提交整柜完成：失败 ${job.result.failedCount}`);
    },
    onError: (error) => setSnackbar(error instanceof Error ? error.message : "提交整柜失败"),
  });

  const pushHqMutation = useMutation({
    mutationFn: async () => {
      if (!selectedDetails.length) throw new Error("请先选择要推送 HQ 的明细");
      const items = toPushProductsToHqItems(selectedDetails);
      const productCodes = items.map((item) => item.productCode ?? "").filter(Boolean);
      const job = await createPushProductsToHqJob({
        productCodes,
        items,
        operationId: buildPushProductsToHqOperationId(containerGuid, productCodes, items.length),
      });
      return waitPushProductsToHqJob(job.jobId);
    },
    onSuccess: (job) => setSnackbar(job.message ?? "推送 HQ 任务已完成"),
    onError: (error) => setSnackbar(error instanceof Error ? error.message : "推送 HQ 失败"),
  });

  const alignDomesticProductCodeMutation = useMutation({
    mutationFn: (detail: ContainerDetail) => {
      const detailHguid = getDetailGuid(detail).trim();
      const localProductCode = getDetailLocalProductCode(detail);
      const domesticProductCode = getDetailDomesticProductCode(detail);
      if (!detailHguid || !localProductCode || !domesticProductCode) {
        throw new Error("缺少可对齐的商品编码");
      }
      return alignDomesticProductCode({
        detailHguid,
        expectedDomesticProductCode: domesticProductCode,
        targetProductCode: localProductCode,
        supplierCode: getDetailLocalSupplierCode(detail),
      });
    },
    onSuccess: (result) => {
      invalidateDetail();
      setSnackbar(`已对齐国内商品编码 ${result.oldProductCode || ""} -> ${result.newProductCode || ""}`);
    },
    onError: (error) => setSnackbar(error instanceof Error ? error.message : "对齐国内商品编码失败"),
    onSettled: () => setAligningDetailHguid(""),
  });

  const exportMutation = useMutation({
    mutationFn: (format: ContainerExportFormat) =>
      exportContainerDetails(containerGuid, {
        format,
        query: detailQueryPayload,
        selectedHguids,
        columns: format === "pdf"
          ? [...DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMNS]
          : [...DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMNS],
        fileNameHint: headerQuery.data?.货柜编号 || containerGuid,
      }),
    onSuccess: (result) => setSnackbar(`已导出 ${result.fileName}`),
    onError: (error) => setSnackbar(error instanceof Error ? error.message : "导出失败"),
  });

  const handleAlignDomesticProductCode = (detail: ContainerDetail) => {
    const detailHguid = getDetailGuid(detail).trim();
    const localProductCode = getDetailLocalProductCode(detail);
    const domesticProductCode = getDetailDomesticProductCode(detail);
    if (alignDomesticProductCodeMutation.isPending) {
      return;
    }
    if (!detailHguid || !localProductCode || !domesticProductCode) {
      setSnackbar("缺少可对齐的商品编码");
      return;
    }

    Alert.alert(
      "对齐国内商品编码",
      `确认将国内商品编码 ${domesticProductCode} 对齐为 ${localProductCode}？`,
      [
        { text: "取消", style: "cancel" },
        {
          text: "对齐编码",
          onPress: () => {
            setAligningDetailHguid(detailHguid);
            alignDomesticProductCodeMutation.mutate(detail);
          },
        },
      ],
    );
  };

  const confirmWholeFilterMutation = (
    action: "float" | "prices" | "recalculate" | "backfill",
    title: string,
  ) => {
    if (selectedHguids.length) {
      bulkMutation.mutate(action);
      return;
    }

    Alert.alert(
      title,
      `未选择明细，将作用于当前筛选结果 ${filteredItemCount} 条。`,
      [
        { text: "取消", style: "cancel" },
        {
          text: "确认执行",
          style: "destructive",
          onPress: () => bulkMutation.mutate(action),
        },
      ],
    );
  };

  const toggleSelection = (hguid: string) => {
    if (!hguid) return;
    setSelectedHguids((current) => (
      current.includes(hguid)
        ? current.filter((item) => item !== hguid)
        : [...current, hguid]
    ));
  };

  const toggleCurrentPage = () => {
    // 本页全选只作用于当前加载明细，避免跨页批量操作误伤。
    setSelectedHguids((current) => toggleCurrentPageSelection(current, details));
  };

  const updateRangeFilter = (field: keyof DetailRangeFilterForm, value: string) => {
    setRangeFilters((current) => ({ ...current, [field]: value }));
  };

  const applySearch = () => {
    if (hasInvalidRangeFilters(rangeFilters)) {
      setSnackbar("筛选范围存在无效数字");
      return;
    }
    setPage(1);
    setAppliedKeyword(keyword.trim());
    setAppliedRangeFilters({ ...rangeFilters });
    setSelectedHguids([]);
  };

  const clearRangeFilters = () => {
    setRangeFilters(EMPTY_DETAIL_RANGE_FILTERS);
    setAppliedRangeFilters(EMPTY_DETAIL_RANGE_FILTERS);
    setPage(1);
    setSelectedHguids([]);
  };

  const changePage = (nextPage: number) => {
    setSelectedHguids([]);
    setPage(Math.max(1, nextPage));
  };

  if (!access.canViewContainers) {
    return (
      <SafeAreaView style={styles.safeArea}>
        <EmptyState title="无权访问货柜" description="请联系管理员开通货柜查看权限" />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.safeArea}>
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={
          <RefreshControl
            refreshing={headerQuery.isRefetching || productsQuery.isRefetching}
            onRefresh={() => {
              void headerQuery.refetch();
              void productsQuery.refetch();
            }}
          />
        }
      >
        <Surface style={styles.headerPanel} mode="flat">
          <Button icon="arrow-left" onPress={() => router.back()}>返回</Button>
          {headerQuery.isLoading ? (
            <ActivityIndicator />
          ) : (
            <>
              <Text variant="titleLarge">{headerQuery.data?.货柜编号 ?? containerGuid}</Text>
              <View style={styles.metricGrid}>
                <DetailMetric label="预计到岸" value={formatDate(headerQuery.data?.预计到岸日期)} />
                <DetailMetric label="实际到货" value={formatDate(headerQuery.data?.实际到货日期)} />
                <DetailMetric label="件数" value={formatNumber(headerQuery.data?.合计件数, 0)} />
                <DetailMetric label="金额" value={formatNumber(headerQuery.data?.合计金额)} />
              </View>
            </>
          )}
        </Surface>

        <Surface style={styles.filterPanel} mode="flat">
          <TextInput
            mode="outlined"
            label="搜索货号"
            value={keyword}
            onChangeText={setKeyword}
            right={<TextInput.Icon icon="magnify" onPress={applySearch} />}
            onSubmitEditing={applySearch}
          />
          <View style={styles.chipRow}>
            {TAGS.map((tag) => {
              const selected = tag.value === "all" ? selectedTags.length === 0 : selectedTags.includes(tag.value);
              const count = productsQuery.data?.tagStats[tag.value] ?? 0;
              return (
                <Chip
                  key={tag.value}
                  selected={selected}
                  onPress={() => {
                    setPage(1);
                    setSelectedHguids([]);
                    setSelectedTags((current) => toggleSelectedTag(current, tag.value));
                  }}
                >
                  {tag.label}{tag.value === "all" ? ` ${productsQuery.data?.itemsTotal ?? 0}` : ` ${count}`}
                </Chip>
              );
            })}
          </View>
          <View style={styles.switchRowCompact}>
            <Text style={styles.muted}>只读零售价</Text>
            <Switch value={showReadonlyOemPrice} onValueChange={setShowReadonlyOemPrice} />
          </View>
          <View style={styles.filterToggleRow}>
            <Button
              compact
              icon="filter-variant"
              mode={rangeFilterActive ? "contained-tonal" : "text"}
              onPress={() => setShowRangeFilters((value) => !value)}
            >
              {rangeFilterActive ? "范围筛选已启用" : "范围筛选"}
            </Button>
            {rangeFilterActive ? <Button compact mode="text" onPress={clearRangeFilters}>清空范围</Button> : null}
          </View>
          {showRangeFilters ? (
            <View style={styles.rangeFilterPanel}>
              <View style={styles.inputRow}>
                <TextInput
                  mode="outlined"
                  label="数量下限"
                  keyboardType="decimal-pad"
                  value={rangeFilters.containerQuantityMin}
                  onChangeText={(value) => updateRangeFilter("containerQuantityMin", value)}
                  style={styles.inputHalf}
                />
                <TextInput
                  mode="outlined"
                  label="数量上限"
                  keyboardType="decimal-pad"
                  value={rangeFilters.containerQuantityMax}
                  onChangeText={(value) => updateRangeFilter("containerQuantityMax", value)}
                  style={styles.inputHalf}
                />
              </View>
              <View style={styles.inputRow}>
                <TextInput
                  mode="outlined"
                  label="中包下限"
                  keyboardType="decimal-pad"
                  value={rangeFilters.middlePackQuantityMin}
                  onChangeText={(value) => updateRangeFilter("middlePackQuantityMin", value)}
                  style={styles.inputHalf}
                />
                <TextInput
                  mode="outlined"
                  label="中包上限"
                  keyboardType="decimal-pad"
                  value={rangeFilters.middlePackQuantityMax}
                  onChangeText={(value) => updateRangeFilter("middlePackQuantityMax", value)}
                  style={styles.inputHalf}
                />
              </View>
              <View style={styles.inputRow}>
                <TextInput
                  mode="outlined"
                  label="实时进货价下限"
                  keyboardType="decimal-pad"
                  value={rangeFilters.warehouseImportPriceMin}
                  onChangeText={(value) => updateRangeFilter("warehouseImportPriceMin", value)}
                  style={styles.inputHalf}
                />
                <TextInput
                  mode="outlined"
                  label="实时进货价上限"
                  keyboardType="decimal-pad"
                  value={rangeFilters.warehouseImportPriceMax}
                  onChangeText={(value) => updateRangeFilter("warehouseImportPriceMax", value)}
                  style={styles.inputHalf}
                />
              </View>
              <View style={styles.inputRow}>
                <TextInput
                  mode="outlined"
                  label="零售价下限"
                  keyboardType="decimal-pad"
                  value={rangeFilters.oemPriceMin}
                  onChangeText={(value) => updateRangeFilter("oemPriceMin", value)}
                  style={styles.inputHalf}
                />
                <TextInput
                  mode="outlined"
                  label="零售价上限"
                  keyboardType="decimal-pad"
                  value={rangeFilters.oemPriceMax}
                  onChangeText={(value) => updateRangeFilter("oemPriceMax", value)}
                  style={styles.inputHalf}
                />
              </View>
              <View style={styles.actionRow}>
                <Button mode="contained" icon="filter-check" onPress={applySearch}>应用筛选</Button>
                <Button mode="text" icon="filter-remove" onPress={clearRangeFilters}>清空范围</Button>
              </View>
            </View>
          ) : null}
          <View style={styles.actionRow}>
            <Menu
              visible={bulkMenuVisible}
              onDismiss={() => setBulkMenuVisible(false)}
              anchor={<Button icon="dots-vertical" mode="contained" onPress={() => setBulkMenuVisible(true)}>批量操作</Button>}
            >
              {canEditContainer || canDeleteContainer ? (
                <>
                  {canEditContainer ? (
                    <>
                      <Menu.Item title="批量调浮率" onPress={() => { setBulkMenuVisible(false); setBulkModalType("float"); }} />
                      <Menu.Item title="批量改价" onPress={() => { setBulkMenuVisible(false); setBulkModalType("prices"); }} />
                      <Menu.Item title="重算成本" onPress={() => { setBulkMenuVisible(false); confirmWholeFilterMutation("recalculate", "重算成本"); }} />
                      <Menu.Item title="回填上次价格" onPress={() => { setBulkMenuVisible(false); confirmWholeFilterMutation("backfill", "回填上次价格"); }} />
                    </>
                  ) : null}
                  {canDeleteContainer ? (
                    <>
                      <Divider />
                      <Menu.Item title="删除所选" onPress={() => { setBulkMenuVisible(false); bulkMutation.mutate("delete"); }} />
                    </>
                  ) : null}
                  <Divider />
                </>
              ) : null}
              <Menu.Item title="导出 Excel" onPress={() => { setBulkMenuVisible(false); exportMutation.mutate("excel"); }} />
              <Menu.Item title="导出 PDF" onPress={() => { setBulkMenuVisible(false); exportMutation.mutate("pdf"); }} />
            </Menu>
            {canRunProductJobs ? (
              <>
                <Button
                  icon="plus-box"
                  mode="outlined"
                  loading={createProductsMutation.isPending}
                  disabled={createProductsMutation.isPending}
                  onPress={() => createProductsMutation.mutate()}
                >
                  创建新商品
                </Button>
                <Button
                  icon="check-decagram"
                  mode="outlined"
                  loading={submitMutation.isPending}
                  disabled={submitMutation.isPending}
                  onPress={() => submitMutation.mutate()}
                >
                  提交整柜
                </Button>
              </>
            ) : null}
            {canRunProductJobs ? (
              <Button
                icon="cloud-upload"
                mode="outlined"
                loading={pushHqMutation.isPending}
                disabled={pushHqMutation.isPending || selectedHguids.length === 0}
                onPress={() => pushHqMutation.mutate()}
              >
                推送已选 HQ
              </Button>
            ) : null}
          </View>
          <View style={styles.selectionSummaryRow}>
            <Text style={styles.muted}>已选 {selectedHguids.length} 条，本页已选 {currentPageSelectedCount} 条</Text>
            <Button
              compact
              mode="text"
              disabled={currentPageDetailGuids.length === 0}
              onPress={toggleCurrentPage}
            >
              {isCurrentPageFullySelected ? "取消本页" : "本页全选"}
            </Button>
          </View>
        </Surface>

        {productsQuery.isLoading ? (
          <ActivityIndicator style={styles.loading} />
        ) : details.length ? (
          details.map((detail) => {
            const hguid = getDetailGuid(detail).trim();
            return (
              <DetailCard
                key={hguid || detail.id || detail.ID}
                detail={detail}
                selected={selectedSet.has(hguid)}
                canEditContainer={canEditContainer}
                canAlignDomesticProductCode={canAlignDomesticProductCode}
                showReadonlyOemPrice={showReadonlyOemPrice}
                aligning={aligningDetailHguid === hguid && alignDomesticProductCodeMutation.isPending}
                alignDisabled={alignDomesticProductCodeMutation.isPending}
                onToggle={() => toggleSelection(hguid)}
                onAlign={() => handleAlignDomesticProductCode(detail)}
                onEdit={() => {
                  setEditingDetail(detail);
                  setEditForm(buildEditForm(detail));
                }}
              />
            );
          })
        ) : (
          <EmptyState title="没有明细" description="调整搜索或标签后再试" />
        )}

        <View style={styles.pagination}>
          <Button mode="outlined" disabled={page <= 1} onPress={() => changePage(page - 1)}>
            上一页
          </Button>
          <Text style={styles.pageText}>{page} / {totalPages}</Text>
          <Button
            mode="outlined"
            disabled={!productsQuery.data?.hasMore}
            onPress={() => changePage(page + 1)}
          >
            下一页
          </Button>
        </View>
      </ScrollView>

      <Portal>
        <Modal visible={Boolean(editingDetail && editForm)} onDismiss={() => setEditingDetail(null)} contentContainerStyle={styles.modal}>
          <Text variant="titleMedium">编辑明细</Text>
          {editForm ? (
            <>
              <TextInput mode="outlined" label="中文名称" value={editForm.productName} onChangeText={(value) => setEditForm((current) => current && { ...current, productName: value })} />
              <TextInput mode="outlined" label="英文名称" value={editForm.englishName} onChangeText={(value) => setEditForm((current) => current && { ...current, englishName: value })} />
              <View style={styles.inputRow}>
                <TextInput mode="outlined" label="国内价" keyboardType="decimal-pad" value={editForm.domesticPrice} onChangeText={(value) => setEditForm((current) => current && { ...current, domesticPrice: value })} style={styles.inputHalf} />
                <TextInput mode="outlined" label="进口价" keyboardType="decimal-pad" value={editForm.importPrice} onChangeText={(value) => setEditForm((current) => current && { ...current, importPrice: value })} style={styles.inputHalf} />
              </View>
              <View style={styles.inputRow}>
                <TextInput mode="outlined" label="零售价" keyboardType="decimal-pad" value={editForm.oemPrice} onChangeText={(value) => setEditForm((current) => current && { ...current, oemPrice: value })} style={styles.inputHalf} />
                <TextInput mode="outlined" label="浮率" keyboardType="decimal-pad" value={editForm.floatRate} onChangeText={(value) => setEditForm((current) => current && { ...current, floatRate: value })} style={styles.inputHalf} />
              </View>
              <View style={styles.inputRow}>
                <TextInput mode="outlined" label="装柜数量" keyboardType="decimal-pad" value={editForm.containerQuantity} onChangeText={(value) => setEditForm((current) => current && { ...current, containerQuantity: value })} style={styles.inputHalf} />
                <TextInput mode="outlined" label="中包数" keyboardType="decimal-pad" value={editForm.middlePackQuantity} onChangeText={(value) => setEditForm((current) => current && { ...current, middlePackQuantity: value })} style={styles.inputHalf} />
              </View>
              <View style={styles.switchRow}>
                <Text>启用</Text>
                <Switch value={editForm.isActive} onValueChange={(value) => setEditForm((current) => current && { ...current, isActive: value })} />
              </View>
            </>
          ) : null}
          <View style={styles.actionRow}>
            <Button onPress={() => setEditingDetail(null)}>取消</Button>
            <Button mode="contained" loading={updateMutation.isPending} onPress={() => updateMutation.mutate()}>保存</Button>
          </View>
        </Modal>

        <Modal visible={Boolean(bulkModalType)} onDismiss={() => setBulkModalType(null)} contentContainerStyle={styles.modal}>
          <Text variant="titleMedium">{bulkModalType === "float" ? "批量调浮率" : "批量改价"}</Text>
          {bulkModalType === "float" ? (
            <TextInput mode="outlined" label="浮率" keyboardType="decimal-pad" value={bulkFloatRate} onChangeText={setBulkFloatRate} />
          ) : (
            <>
              <TextInput mode="outlined" label="进口价" keyboardType="decimal-pad" value={bulkImportPrice} onChangeText={setBulkImportPrice} />
              <TextInput mode="outlined" label="零售价" keyboardType="decimal-pad" value={bulkOemPrice} onChangeText={setBulkOemPrice} />
            </>
          )}
          <Text style={styles.muted}>{selectedHguids.length ? `作用于已选 ${selectedHguids.length} 条` : "未选择时作用于当前筛选结果"}</Text>
          <View style={styles.actionRow}>
            <Button onPress={() => setBulkModalType(null)}>取消</Button>
            <Button
              mode="contained"
              loading={bulkMutation.isPending}
              onPress={() => {
                if (!bulkModalType) return;
                confirmWholeFilterMutation(
                  bulkModalType === "float" ? "float" : "prices",
                  bulkModalType === "float" ? "批量调浮率" : "批量改价",
                );
              }}
            >
              执行
            </Button>
          </View>
        </Modal>
      </Portal>
      <Snackbar visible={Boolean(snackbar)} onDismiss={() => setSnackbar("")}>{snackbar}</Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: "#F6F8FB",
  },
  content: {
    gap: 12,
    padding: 12,
    paddingBottom: 28,
  },
  headerPanel: {
    gap: 10,
    padding: 12,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  filterPanel: {
    gap: 10,
    padding: 12,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  card: {
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  chipRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
  },
  selectionSummaryRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  filterToggleRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  rangeFilterPanel: {
    gap: 8,
  },
  actionRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    alignItems: "center",
  },
  metricGrid: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 10,
  },
  metric: {
    minWidth: 92,
    flexGrow: 1,
  },
  muted: {
    color: "#64748B",
  },
  remark: {
    marginTop: 10,
    color: "#475569",
  },
  warningText: {
    marginTop: 10,
    color: "#B45309",
  },
  detailImageFrame: {
    width: 72,
    height: 72,
    marginRight: 12,
    overflow: "hidden",
    borderRadius: 8,
    backgroundColor: "#EAEFF3",
  },
  detailImage: {
    width: "100%",
    height: "100%",
  },
  detailImagePlaceholder: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#EAEFF3",
  },
  imagePlaceholderText: {
    color: "#64748B",
  },
  loading: {
    marginVertical: 28,
  },
  pagination: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: 16,
  },
  pageText: {
    minWidth: 70,
    textAlign: "center",
  },
  modal: {
    margin: 18,
    gap: 10,
    padding: 16,
    borderRadius: 8,
    backgroundColor: "#FFFFFF",
  },
  inputRow: {
    flexDirection: "row",
    gap: 10,
  },
  inputHalf: {
    flex: 1,
  },
  switchRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  switchRowCompact: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "flex-start",
    gap: 8,
  },
});
