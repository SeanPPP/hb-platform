import { useEffect, useMemo, useState } from "react";
import {
  Image,
  RefreshControl,
  ScrollView,
  StyleSheet,
  View,
} from "react-native";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  HelperText,
  Modal,
  Portal,
  SegmentedButtons,
  Snackbar,
  Surface,
  Switch,
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
  createAdvertisement,
  createAdvertisementUploadSignature,
  deleteAdvertisement,
  fetchAdvertisementDetail,
  fetchAdvertisements,
  setAdvertisementEnabled,
  updateAdvertisement,
} from "@/modules/advertisements/api";
import { uploadAdvertisementAssetToSignedUrl } from "@/modules/advertisements/upload";
import type {
  AdvertisementDraft,
  AdvertisementItem,
  AdvertisementMediaType,
  AdvertisementUpsertPayload,
} from "@/modules/advertisements/types";
import { getDeviceBoundStoreCode } from "@/modules/shop/device-bound-store-filter";
import { useStores } from "@/modules/shop/use-stores";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { resolveLocaleTag } from "@/shared/i18n/types";
import { useAuthStore } from "@/store/auth-store";

const PAGE_SIZE = 20;

type FilterEnabledValue = "all" | "enabled" | "disabled";

function createEmptyDraft(): AdvertisementDraft {
  return {
    id: null,
    title: "",
    description: "",
    mediaType: "image",
    mediaUrl: "",
    thumbnailUrl: "",
    objectKey: "",
    originalFileName: "",
    contentType: "",
    fileSize: "",
    effectiveStart: "",
    effectiveEnd: "",
    isEnabled: true,
    sortOrder: "0",
    storeCodes: [],
  };
}

function mapAdvertisementToDraft(item: AdvertisementItem): AdvertisementDraft {
  return {
    id: item.id,
    title: item.title,
    description: item.description,
    mediaType: item.mediaType,
    mediaUrl: item.mediaUrl,
    thumbnailUrl: item.thumbnailUrl,
    objectKey: item.objectKey,
    originalFileName: item.originalFileName,
    contentType: item.contentType,
    fileSize: item.fileSize == null ? "" : String(item.fileSize),
    effectiveStart: item.effectiveStart,
    effectiveEnd: item.effectiveEnd,
    isEnabled: item.isEnabled,
    sortOrder: item.sortOrder == null ? "0" : String(item.sortOrder),
    storeCodes: item.stores.map((store) => store.storeCode).filter(Boolean),
  };
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

function formatFileSize(value?: number | null) {
  if (value == null || !Number.isFinite(value)) {
    return "--";
  }
  if (value < 1024) {
    return `${value} B`;
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`;
  }
  return `${(value / (1024 * 1024)).toFixed(1)} MB`;
}

function buildPayloadFromDraft(draft: AdvertisementDraft): AdvertisementUpsertPayload {
  return {
    title: draft.title,
    description: draft.description,
    mediaType: draft.mediaType,
    mediaUrl: draft.mediaUrl,
    thumbnailUrl: draft.thumbnailUrl,
    objectKey: draft.objectKey,
    originalFileName: draft.originalFileName,
    contentType: draft.contentType,
    fileSize: draft.fileSize ? Number(draft.fileSize) : null,
    effectiveStart: draft.effectiveStart,
    effectiveEnd: draft.effectiveEnd,
    isEnabled: draft.isEnabled,
    sortOrder: draft.sortOrder ? Number(draft.sortOrder) : 0,
    stores: draft.storeCodes.map((storeCode) => ({ storeCode })),
  };
}

function validateDraft(draft: AdvertisementDraft) {
  const errors: Partial<Record<keyof AdvertisementDraft, string>> = {};
  if (!draft.title.trim()) {
    errors.title = "title";
  }
  if (!draft.mediaUrl.trim()) {
    errors.mediaUrl = "mediaUrl";
  }
  if (!draft.effectiveStart.trim()) {
    errors.effectiveStart = "effectiveStart";
  }
  if (!draft.effectiveEnd.trim()) {
    errors.effectiveEnd = "effectiveEnd";
  }
  if (draft.storeCodes.length === 0) {
    errors.storeCodes = "storeCodes";
  }
  const startTime = new Date(draft.effectiveStart).getTime();
  const endTime = new Date(draft.effectiveEnd).getTime();
  if (draft.effectiveStart.trim() && !Number.isFinite(startTime)) {
    errors.effectiveStart = "effectiveStartInvalid";
  }
  if (draft.effectiveEnd.trim() && !Number.isFinite(endTime)) {
    errors.effectiveEnd = "effectiveEndInvalid";
  }
  if (Number.isFinite(startTime) && Number.isFinite(endTime) && startTime > endTime) {
    errors.effectiveEnd = "effectiveEndOrder";
  }
  return errors;
}

function FieldButton({
  label,
  value,
  placeholder,
  onPress,
}: {
  label: string;
  value?: string | null;
  placeholder: string;
  onPress: () => void;
}) {
  return (
    <Surface style={styles.fieldSurface}>
      <Text variant="labelMedium" style={styles.fieldLabel}>
        {label}
      </Text>
      <Button mode="outlined" onPress={onPress} contentStyle={styles.fieldButtonContent}>
        {value?.trim() ? value : placeholder}
      </Button>
    </Surface>
  );
}

export function AdvertisementsScreen() {
  const { t, language } = useAppTranslation(["advertisements", "common"]);
  const localeTag = useMemo(() => resolveLocaleTag(language), [language]);
  const queryClient = useQueryClient();
  const access = useAuthStore((state) => state.access);
  const managedStoreCodes = useMemo(
    () => access.managedStoreCodes()?.filter(Boolean) ?? [],
    [access]
  );
  const {
    stores,
    selectedStoreCode,
    isDeviceMode,
    isLoading: storesLoading,
  } = useStores();
  const [pageNumber, setPageNumber] = useState(1);
  const [filterTitle, setFilterTitle] = useState("");
  const [filterStoreCode, setFilterStoreCode] = useState("");
  const [filterMediaType, setFilterMediaType] = useState<"all" | AdvertisementMediaType>("all");
  const [filterEnabled, setFilterEnabled] = useState<FilterEnabledValue>("all");
  const [selectedStorePicker, setSelectedStorePicker] = useState<"filter" | "form" | null>(null);
  const [mediaTypePickerVisible, setMediaTypePickerVisible] = useState(false);
  const [editorVisible, setEditorVisible] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<AdvertisementDraft>(createEmptyDraft);
  const [submitAttempted, setSubmitAttempted] = useState(false);
  const [snackbar, setSnackbar] = useState("");
  const deviceBoundStoreCode = getDeviceBoundStoreCode({ isDeviceMode, selectedStoreCode });
  const effectiveFilterStoreCode = deviceBoundStoreCode ?? filterStoreCode;

  const advertisementsQuery = useQuery({
    queryKey: [
      "advertisements",
      pageNumber,
      filterTitle,
      effectiveFilterStoreCode,
      filterMediaType,
      filterEnabled,
    ],
    queryFn: () =>
      fetchAdvertisements({
        pageNumber,
        pageSize: PAGE_SIZE,
        title: filterTitle,
        storeCode: effectiveFilterStoreCode,
        mediaType: filterMediaType === "all" ? undefined : filterMediaType,
        isEnabled:
          filterEnabled === "all"
            ? null
            : filterEnabled === "enabled",
      }),
  });

  useEffect(() => {
    if (!deviceBoundStoreCode) {
      return;
    }

    setFilterStoreCode(deviceBoundStoreCode);
    setDraft((current) =>
      current.storeCodes.length === 1 && current.storeCodes[0] === deviceBoundStoreCode
        ? current
        : { ...current, storeCodes: [deviceBoundStoreCode] }
    );
  }, [deviceBoundStoreCode]);

  const detailQuery = useQuery({
    queryKey: ["advertisement-detail", editingId],
    enabled: Boolean(editingId && editorVisible),
    queryFn: () => fetchAdvertisementDetail(editingId!),
  });

  useEffect(() => {
    if (!detailQuery.data || !editingId) {
      return;
    }
    setDraft(mapAdvertisementToDraft(detailQuery.data));
  }, [detailQuery.data, editingId]);

  const saveMutation = useMutation({
    mutationFn: async (payload: AdvertisementUpsertPayload) => {
      if (editingId) {
        return updateAdvertisement(editingId, payload);
      }
      return createAdvertisement(payload);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["advertisements"] });
      if (editingId) {
        void queryClient.invalidateQueries({ queryKey: ["advertisement-detail", editingId] });
      }
      setEditorVisible(false);
      setEditingId(null);
      setDraft(createEmptyDraft());
      setSubmitAttempted(false);
      setSnackbar(t("messages.saveSuccess"));
    },
    onError: () => {
      setSnackbar(t("messages.saveFailed"));
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteAdvertisement(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["advertisements"] });
      setSnackbar(t("messages.deleteSuccess"));
    },
    onError: () => {
      setSnackbar(t("messages.deleteFailed"));
    },
  });

  const enableMutation = useMutation({
    mutationFn: ({ id, enable }: { id: string; enable: boolean }) =>
      setAdvertisementEnabled(id, enable),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["advertisements"] });
      if (editingId) {
        void queryClient.invalidateQueries({ queryKey: ["advertisement-detail", editingId] });
      }
      setSnackbar(t("messages.toggleSuccess"));
    },
    onError: () => {
      setSnackbar(t("messages.toggleFailed"));
    },
  });

  const uploadMutation = useMutation({
    mutationFn: async () => {
      const ImagePicker = await import("expo-image-picker");
      const permission = await ImagePicker.requestMediaLibraryPermissionsAsync();
      if (!permission.granted) {
        throw new Error("permission-denied");
      }

      const result = await ImagePicker.launchImageLibraryAsync({
        mediaTypes: ["images", "videos"],
        allowsEditing: false,
        quality: 1,
        selectionLimit: 1,
      });

      if (result.canceled || !result.assets?.[0]) {
        return null;
      }

      const asset = result.assets[0];
      const fileName = asset.fileName || `advertisement-${Date.now()}`;
      const contentType =
        asset.mimeType ||
        (asset.type === "video" ? "video/mp4" : "image/jpeg");
      const fileSize =
        typeof asset.fileSize === "number"
          ? asset.fileSize
          : (await fetch(asset.uri).then((response) => response.blob())).size;
      const signature = await createAdvertisementUploadSignature({
        fileName,
        contentType,
        fileSize,
      });
      const uploaded = await uploadAdvertisementAssetToSignedUrl(asset.uri, signature);

      return {
        uploaded,
        asset,
        fileName,
        contentType,
        fileSize,
      };
    },
    onSuccess: (result) => {
      if (!result) {
        return;
      }

      setDraft((current) => {
        const nextMediaType = result.asset.type === "video" ? "video" : "image";
        const mediaUrl = uploadedOrCurrent(result.uploaded.mediaUrl, current.mediaUrl);
        return {
          ...current,
          mediaType: nextMediaType,
          mediaUrl,
          thumbnailUrl:
            nextMediaType === "image"
              ? mediaUrl
              : current.thumbnailUrl,
          objectKey: result.uploaded.objectKey,
          originalFileName: result.fileName,
          contentType: result.contentType,
          fileSize: String(result.fileSize),
        };
      });
      setSnackbar(t("messages.uploadSuccess"));
    },
    onError: (error) => {
      const message =
        error instanceof Error && error.message === "permission-denied"
          ? t("messages.permissionDenied")
          : t("messages.uploadFailed");
      setSnackbar(message);
    },
  });

  const errors = useMemo(() => validateDraft(draft), [draft]);
  const selectedFilterStore =
    stores.find((store) => store.storeCode === effectiveFilterStoreCode) ?? null;
  const selectedDraftStores = useMemo(
    () =>
      draft.storeCodes
        .map((storeCode) => stores.find((store) => store.storeCode === storeCode))
        .filter((store): store is NonNullable<typeof store> => Boolean(store)),
    [draft.storeCodes, stores]
  );
  const allDraftStoresSelected =
    stores.length > 0 && draft.storeCodes.length === stores.length;

  const mediaTypeItems: SelectionListItem[] = useMemo(
    () => [
      { key: "image", label: t("mediaTypes.image") },
      { key: "video", label: t("mediaTypes.video") },
    ],
    [t]
  );

  function updateDraft<K extends keyof AdvertisementDraft>(key: K, value: AdvertisementDraft[K]) {
    setDraft((current) => ({
      ...current,
      [key]: value,
    }));
  }

  function resetDraft() {
    setEditingId(null);
    setDraft({
      ...createEmptyDraft(),
      storeCodes: deviceBoundStoreCode
        ? [deviceBoundStoreCode]
        : managedStoreCodes.length > 0
          ? managedStoreCodes
          : [],
    });
    setSubmitAttempted(false);
  }

  function openCreateEditor() {
    resetDraft();
    setEditorVisible(true);
  }

  function openEditEditor(item: AdvertisementItem) {
    setEditingId(item.id);
    setDraft(mapAdvertisementToDraft(item));
    setSubmitAttempted(false);
    setEditorVisible(true);
  }

  function toggleDraftStore(storeCode: string) {
    if (deviceBoundStoreCode) {
      setDraft((current) => ({ ...current, storeCodes: [deviceBoundStoreCode] }));
      return;
    }

    setDraft((current) => ({
      ...current,
      storeCodes: current.storeCodes.includes(storeCode)
        ? current.storeCodes.filter((item) => item !== storeCode)
        : [...current.storeCodes, storeCode],
    }));
  }

  function toggleAllDraftStores() {
    if (deviceBoundStoreCode) {
      setDraft((current) => ({ ...current, storeCodes: [deviceBoundStoreCode] }));
      return;
    }

    setDraft((current) => ({
      ...current,
      storeCodes: allDraftStoresSelected
        ? []
        : stores.map((store) => store.storeCode).filter(Boolean),
    }));
  }

  function onSubmit() {
    setSubmitAttempted(true);
    const nextErrors = validateDraft(draft);
    if (Object.keys(nextErrors).length > 0) {
      setSnackbar(t("messages.validationFailed"));
      return;
    }
    saveMutation.mutate(buildPayloadFromDraft(draft));
  }

  const items = advertisementsQuery.data?.items ?? [];
  const total = advertisementsQuery.data?.total ?? 0;
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE));

  if (!access.canViewAdvertisements) {
    return (
      <SafeAreaView style={styles.safeArea} edges={["top"]}>
        <EmptyState
          title={t("states.noAccessTitle")}
          description={t("states.noAccessDescription")}
        />
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.safeArea} edges={["top"]}>
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={
          <RefreshControl
            refreshing={advertisementsQuery.isRefetching}
            onRefresh={() => void advertisementsQuery.refetch()}
          />
        }
      >
        <Surface style={styles.hero}>
          <Text variant="headlineSmall">{t("title")}</Text>
          <Text variant="bodyMedium" style={styles.subtitle}>
            {t("subtitle")}
          </Text>
          <View style={styles.filterRow}>
            <TextInput
              mode="outlined"
              label={t("filters.title")}
              value={filterTitle}
              onChangeText={setFilterTitle}
              style={styles.flexField}
            />
            <FieldButton
              label={t("filters.store")}
              value={selectedFilterStore?.storeName}
              placeholder={t("filters.storePlaceholder")}
              onPress={() => setSelectedStorePicker("filter")}
            />
          </View>
          <View style={styles.segmentRow}>
            <SegmentedButtons
              value={filterMediaType}
              onValueChange={(value) => setFilterMediaType(value as "all" | AdvertisementMediaType)}
              buttons={[
                { value: "all", label: t("filters.allMediaTypes") },
                { value: "image", label: t("mediaTypes.image") },
                { value: "video", label: t("mediaTypes.video") },
              ]}
            />
          </View>
          <View style={styles.segmentRow}>
            <SegmentedButtons
              value={filterEnabled}
              onValueChange={(value) => setFilterEnabled(value as FilterEnabledValue)}
              buttons={[
                { value: "all", label: t("filters.allStatuses") },
                { value: "enabled", label: t("statuses.enabled") },
                { value: "disabled", label: t("statuses.disabled") },
              ]}
            />
          </View>
          <View style={styles.actionsRow}>
            <Button mode="outlined" onPress={() => {
              setFilterTitle("");
              setFilterStoreCode("");
              setFilterMediaType("all");
              setFilterEnabled("all");
              setPageNumber(1);
            }}>
              {t("actions.resetFilters")}
            </Button>
            {access.canManageAdvertisements ? (
              <Button mode="contained" onPress={openCreateEditor}>
                {t("actions.create")}
              </Button>
            ) : null}
          </View>
        </Surface>

        {advertisementsQuery.isLoading ? (
          <View style={styles.loadingState}>
            <ActivityIndicator />
          </View>
        ) : advertisementsQuery.isError ? (
          <EmptyState
            title={t("states.loadFailedTitle")}
            description={t("states.loadFailedDescription")}
            actionLabel={t("common:actions.retry")}
            onAction={() => void advertisementsQuery.refetch()}
          />
        ) : items.length === 0 ? (
          <EmptyState
            title={t("states.emptyTitle")}
            description={t("states.emptyDescription")}
          />
        ) : (
          <View style={styles.list}>
            {items.map((item) => (
              <Card key={item.id || item.objectKey || item.title} mode="outlined" style={styles.card}>
                <Card.Content>
                  <View style={styles.cardHeader}>
                    <View style={styles.cardTitleBlock}>
                      <Text variant="titleMedium">{item.title || "--"}</Text>
                      <Text variant="bodySmall" style={styles.cardMeta}>
                        {t(`mediaTypes.${item.mediaType}`)} · {formatDateTime(item.effectiveStart, localeTag)}
                      </Text>
                    </View>
                    <Chip compact>{item.isEnabled ? t("statuses.enabled") : t("statuses.disabled")}</Chip>
                  </View>

                  {item.mediaType === "image" && item.mediaUrl ? (
                    <Image source={{ uri: item.mediaUrl }} style={styles.previewImage} resizeMode="cover" />
                  ) : (
                    <Surface style={styles.videoPlaceholder}>
                      <Text variant="bodyMedium">{t("labels.videoSelected")}</Text>
                      <Text variant="bodySmall" style={styles.cardMeta}>
                        {item.originalFileName || item.mediaUrl || "--"}
                      </Text>
                    </Surface>
                  )}

                  <Text variant="bodyMedium" style={styles.cardDescription}>
                    {item.description || t("labels.noDescription")}
                  </Text>

                  <View style={styles.chipsRow}>
                    {(item.stores.length > 0 ? item.stores : [{ storeCode: t("labels.noStore") }]).map((store) => (
                      <Chip key={`${item.id}-${store.storeCode}`} compact>
                        {store.storeCode}
                      </Chip>
                    ))}
                  </View>

                  <View style={styles.metricsRow}>
                    <Text variant="bodySmall">{t("labels.sortOrder")}: {item.sortOrder ?? 0}</Text>
                    <Text variant="bodySmall">{t("labels.fileSize")}: {formatFileSize(item.fileSize)}</Text>
                  </View>

                  <View style={styles.cardActions}>
                    {access.canManageAdvertisements ? (
                      <>
                        <Button compact mode="contained-tonal" onPress={() => openEditEditor(item)}>
                          {t("common:actions.viewDetail")}
                        </Button>
                        <Button
                          compact
                          mode="outlined"
                          loading={enableMutation.isPending}
                          onPress={() =>
                            enableMutation.mutate({ id: item.id, enable: !item.isEnabled })
                          }
                        >
                          {item.isEnabled ? t("actions.disable") : t("actions.enable")}
                        </Button>
                        <Button
                          compact
                          textColor="#B00020"
                          onPress={() => deleteMutation.mutate(item.id)}
                          loading={deleteMutation.isPending}
                        >
                          {t("actions.delete")}
                        </Button>
                      </>
                    ) : null}
                  </View>
                </Card.Content>
              </Card>
            ))}
          </View>
        )}

        <Surface style={styles.paginationBar}>
          <Text variant="bodySmall">
            {t("pagination.summary", { page: pageNumber, totalPages: pageCount, total })}
          </Text>
          <View style={styles.paginationActions}>
            <Button
              compact
              mode="outlined"
              disabled={pageNumber <= 1}
              onPress={() => setPageNumber((current) => Math.max(1, current - 1))}
            >
              {t("pagination.previous")}
            </Button>
            <Button
              compact
              mode="outlined"
              disabled={pageNumber >= pageCount}
              onPress={() => setPageNumber((current) => Math.min(pageCount, current + 1))}
            >
              {t("pagination.next")}
            </Button>
          </View>
        </Surface>
      </ScrollView>

      <StorePickerModal
        visible={selectedStorePicker === "filter"}
        stores={stores}
        selectedStoreCode={effectiveFilterStoreCode}
        title={t("filters.store")}
        cancelLabel={t("common:actions.cancel")}
        includeAllOption={!deviceBoundStoreCode}
        allLabel={t("filters.storePlaceholder")}
        onDismiss={() => setSelectedStorePicker(null)}
        onSelectStore={(store) => {
          setFilterStoreCode(deviceBoundStoreCode ?? store?.storeCode ?? "");
          setPageNumber(1);
          setSelectedStorePicker(null);
        }}
      />

      <Portal>
        <Modal
          visible={editorVisible}
          onDismiss={() => {
            setEditorVisible(false);
            setEditingId(null);
            setSubmitAttempted(false);
          }}
          contentContainerStyle={styles.modalContainer}
        >
          <ScrollView contentContainerStyle={styles.modalContent}>
            <Text variant="titleLarge">
              {editingId ? t("editor.editTitle") : t("editor.createTitle")}
            </Text>
            <Text variant="bodyMedium" style={styles.subtitle}>
              {t("editor.subtitle")}
            </Text>

            {detailQuery.isLoading && editingId ? (
              <View style={styles.loadingState}>
                <ActivityIndicator />
              </View>
            ) : null}

            <TextInput
              mode="outlined"
              label={t("fields.title")}
              value={draft.title}
              onChangeText={(value) => updateDraft("title", value)}
            />
            <HelperText type="error" visible={submitAttempted && Boolean(errors.title)}>
              {t("validation.title")}
            </HelperText>

            <TextInput
              mode="outlined"
              label={t("fields.description")}
              multiline
              value={draft.description}
              onChangeText={(value) => updateDraft("description", value)}
            />

            <FieldButton
              label={t("fields.mediaType")}
              value={t(`mediaTypes.${draft.mediaType}`)}
              placeholder={t("fields.mediaTypePlaceholder")}
              onPress={() => setMediaTypePickerVisible(true)}
            />

            <View style={styles.uploadRow}>
              <Button
                mode="contained"
                icon="upload"
                onPress={() => uploadMutation.mutate()}
                loading={uploadMutation.isPending}
                disabled={uploadMutation.isPending}
              >
                {t("actions.pickAndUpload")}
              </Button>
              <Text variant="bodySmall" style={styles.cardMeta}>
                {draft.originalFileName || t("labels.noFileSelected")}
              </Text>
            </View>

            {draft.mediaType === "image" && draft.mediaUrl ? (
              <Image source={{ uri: draft.mediaUrl }} style={styles.previewImage} resizeMode="cover" />
            ) : draft.mediaUrl ? (
              <Surface style={styles.videoPlaceholder}>
                <Text variant="bodyMedium">{t("labels.videoSelected")}</Text>
                <Text variant="bodySmall" style={styles.cardMeta}>
                  {draft.mediaUrl}
                </Text>
              </Surface>
            ) : null}

            <TextInput
              mode="outlined"
              label={t("fields.mediaUrl")}
              value={draft.mediaUrl}
              disabled
            />
            <HelperText type="error" visible={submitAttempted && Boolean(errors.mediaUrl)}>
              {t("validation.mediaUrl")}
            </HelperText>

            <TextInput
              mode="outlined"
              label={t("fields.thumbnailUrl")}
              value={draft.thumbnailUrl}
              onChangeText={(value) => updateDraft("thumbnailUrl", value)}
            />
            <TextInput
              mode="outlined"
              label={t("fields.objectKey")}
              value={draft.objectKey}
              disabled
            />
            <TextInput
              mode="outlined"
              label={t("fields.originalFileName")}
              value={draft.originalFileName}
              disabled
            />
            <View style={styles.filterRow}>
              <TextInput
                mode="outlined"
                label={t("fields.contentType")}
                value={draft.contentType}
                disabled
                style={styles.flexField}
              />
              <TextInput
                mode="outlined"
                label={t("fields.fileSize")}
                value={draft.fileSize}
                keyboardType="numeric"
                disabled
                style={styles.flexField}
              />
            </View>

            <TextInput
              mode="outlined"
              label={t("fields.effectiveStart")}
              placeholder="2026-06-01T00:00:00Z"
              value={draft.effectiveStart}
              onChangeText={(value) => updateDraft("effectiveStart", value)}
            />
            <HelperText type="error" visible={submitAttempted && Boolean(errors.effectiveStart)}>
              {t(errors.effectiveStart === "effectiveStartInvalid" ? "validation.effectiveStartInvalid" : "validation.effectiveStart")}
            </HelperText>

            <TextInput
              mode="outlined"
              label={t("fields.effectiveEnd")}
              placeholder="2026-06-30T23:59:59Z"
              value={draft.effectiveEnd}
              onChangeText={(value) => updateDraft("effectiveEnd", value)}
            />
            <HelperText type="error" visible={submitAttempted && Boolean(errors.effectiveEnd)}>
              {t(
                errors.effectiveEnd === "effectiveEndOrder"
                  ? "validation.effectiveEndOrder"
                  : errors.effectiveEnd === "effectiveEndInvalid"
                    ? "validation.effectiveEndInvalid"
                    : "validation.effectiveEnd"
              )}
            </HelperText>

            <TextInput
              mode="outlined"
              label={t("fields.sortOrder")}
              value={draft.sortOrder}
              onChangeText={(value) => updateDraft("sortOrder", value)}
              keyboardType="numeric"
            />

            <View style={styles.switchRow}>
              <Text variant="bodyMedium">{t("fields.isEnabled")}</Text>
              <Switch
                value={draft.isEnabled}
                onValueChange={(value) => updateDraft("isEnabled", value)}
              />
            </View>

            <Surface style={styles.storeScopeBlock}>
              <View style={styles.storeScopeHeader}>
                <Text variant="titleMedium">{t("fields.storeScope")}</Text>
                <View style={styles.storeScopeActions}>
                  <Button
                    mode="outlined"
                    compact
                    disabled={storesLoading || stores.length === 0}
                    onPress={toggleAllDraftStores}
                  >
                    {t(allDraftStoresSelected ? "actions.clearStores" : "actions.selectAllStores")}
                  </Button>
                  <Button mode="outlined" compact onPress={() => setSelectedStorePicker("form")}>
                    {t("actions.addStore")}
                  </Button>
                </View>
              </View>
              <View style={styles.chipsRow}>
                {selectedDraftStores.length > 0 ? (
                  selectedDraftStores.map((store) => (
                    <Chip
                      key={store.storeCode}
                      onClose={() => toggleDraftStore(store.storeCode)}
                    >
                      {store.storeName || store.storeCode}
                    </Chip>
                  ))
                ) : (
                  <Text variant="bodySmall" style={styles.cardMeta}>
                    {storesLoading ? t("states.loadingStores") : t("labels.noStoreSelected")}
                  </Text>
                )}
              </View>
              <HelperText type="error" visible={submitAttempted && Boolean(errors.storeCodes)}>
                {t("validation.storeCodes")}
              </HelperText>
            </Surface>

            <View style={styles.actionsRow}>
              <Button
                mode="outlined"
                onPress={() => {
                  setEditorVisible(false);
                  setEditingId(null);
                  setSubmitAttempted(false);
                }}
              >
                {t("common:actions.cancel")}
              </Button>
              <Button
                mode="contained"
                onPress={onSubmit}
                loading={saveMutation.isPending}
                disabled={saveMutation.isPending}
              >
                {t("common:actions.save")}
              </Button>
            </View>
          </ScrollView>
        </Modal>
      </Portal>

      <StorePickerModal
        visible={selectedStorePicker === "form"}
        stores={stores}
        selectedStoreCode={null}
        title={t("fields.storeScope")}
        cancelLabel={t("common:actions.close")}
        onDismiss={() => setSelectedStorePicker(null)}
        onSelectStore={(store) => {
          if (store?.storeCode) {
            toggleDraftStore(store.storeCode);
          }
        }}
      />

      <SelectionListModal
        visible={mediaTypePickerVisible}
        title={t("fields.mediaType")}
        cancelLabel={t("common:actions.cancel")}
        items={mediaTypeItems}
        selectedKey={draft.mediaType}
        emptyLabel={t("states.emptyMediaTypes")}
        onDismiss={() => setMediaTypePickerVisible(false)}
        onSelect={(item) => {
          if (item?.key === "image" || item?.key === "video") {
            updateDraft("mediaType", item.key);
          }
          setMediaTypePickerVisible(false);
        }}
      />

      <Snackbar visible={Boolean(snackbar)} onDismiss={() => setSnackbar("")} duration={2500}>
        {snackbar}
      </Snackbar>
    </SafeAreaView>
  );
}

function uploadedOrCurrent(nextValue: string, currentValue: string) {
  return nextValue.trim() ? nextValue : currentValue;
}

const styles = StyleSheet.create({
  actionsRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
    justifyContent: "flex-end",
  },
  card: {
    borderRadius: 8,
  },
  cardActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    marginTop: 12,
  },
  cardDescription: {
    marginTop: 12,
  },
  cardHeader: {
    alignItems: "flex-start",
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  cardMeta: {
    color: "#666666",
  },
  cardTitleBlock: {
    flex: 1,
    gap: 4,
  },
  chipsRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    marginTop: 12,
  },
  content: {
    gap: 16,
    padding: 16,
    paddingBottom: 32,
  },
  fieldButtonContent: {
    justifyContent: "flex-start",
    minHeight: 42,
  },
  fieldLabel: {
    color: "#666666",
    marginBottom: 8,
  },
  fieldSurface: {
    borderRadius: 8,
    padding: 12,
  },
  filterRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
  },
  flexField: {
    flex: 1,
    minWidth: 160,
  },
  hero: {
    borderRadius: 8,
    gap: 12,
    padding: 16,
  },
  list: {
    gap: 12,
  },
  loadingState: {
    alignItems: "center",
    justifyContent: "center",
    minHeight: 180,
  },
  metricsRow: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 16,
    marginTop: 12,
  },
  modalContainer: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 8,
    maxHeight: "90%",
    padding: 20,
    width: "92%",
  },
  modalContent: {
    gap: 12,
  },
  paginationActions: {
    flexDirection: "row",
    gap: 12,
  },
  paginationBar: {
    alignItems: "center",
    borderRadius: 8,
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
    justifyContent: "space-between",
    padding: 12,
  },
  previewImage: {
    borderRadius: 8,
    height: 180,
    marginTop: 12,
    width: "100%",
  },
  safeArea: {
    backgroundColor: "#F5F5F5",
    flex: 1,
  },
  segmentRow: {
    gap: 8,
  },
  storeScopeBlock: {
    borderRadius: 8,
    padding: 12,
  },
  storeScopeHeader: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    justifyContent: "space-between",
  },
  storeScopeActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 8,
    justifyContent: "flex-end",
  },
  subtitle: {
    color: "#666666",
  },
  switchRow: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  uploadRow: {
    alignItems: "center",
    flexDirection: "row",
    flexWrap: "wrap",
    gap: 12,
  },
  videoPlaceholder: {
    alignItems: "center",
    borderRadius: 8,
    justifyContent: "center",
    marginTop: 12,
    minHeight: 120,
    padding: 16,
  },
});
