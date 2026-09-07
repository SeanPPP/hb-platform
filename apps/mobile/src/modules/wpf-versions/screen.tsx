import * as Clipboard from "expo-clipboard";
import { useCallback, useEffect, useRef, useState } from "react";
import { Linking, ScrollView, StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import QRCode from "react-native-qrcode-svg";
import {
  ActivityIndicator,
  Button,
  Card,
  Checkbox,
  Divider,
  HelperText,
  IconButton,
  Modal,
  Portal,
  Searchbar,
  SegmentedButtons,
  Snackbar,
  Surface,
  Switch,
  Text,
  TextInput,
  useTheme,
} from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { useAuthStore } from "@/store/auth-store";
import {
  getWpfReleases,
  getWpfTargetDevices,
  getWpfTargetStores,
  saveWpfPolicy,
  updateWpfRelease,
} from "./api";
import {
  canSavePolicy,
  createLatestRequestGuard,
  formatFileSize,
  getErrorMessage,
  getPolicySummary,
  getPolicyValidationError,
  inferRollback,
  maskSha256,
  normalizeTargetScope,
  normalizeVersion,
  policySummaryMatchesRequest,
} from "./logic";
import type {
  WpfDeviceOption,
  WpfPolicySummary,
  WpfRelease,
  WpfReleasePolicyRequest,
  WpfReleaseQuery,
  WpfStoreOption,
} from "./types";

const WEB_WPF_VERSIONS_URL = "https://hotbargain.vip/system/wpf-versions";
const PAGE_SIZE = 10;
const DEVICE_PAGE_SIZE = 30;

type DetailField = [string, string];

function dateText(value: string | null, locale: string) {
  if (!value) return "-";
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf()) ? value : parsed.toLocaleString(locale);
}

function deviceLabel(device: WpfDeviceOption) {
  return [
    device.storeCode ? `[${device.storeCode}]` : "",
    device.systemDeviceNumber || `#${device.deviceRegistrationId}`,
    device.storeName,
    device.remarks,
  ]
    .filter(Boolean)
    .join(" · ");
}

function isSafeDownloadUrl(value: string | null | undefined): value is string {
  if (!value?.trim()) return false;
  try {
    const protocol = new URL(value.trim()).protocol.toLowerCase();
    return protocol === "https:" || protocol === "http:";
  } catch {
    return false;
  }
}

function isValidSha256(value: string) {
  return !value.trim() || /^[a-f\d]{64}$/i.test(value.trim());
}

function normalizeOptionalText(value: string | null | undefined) {
  return value?.trim() || null;
}

function normalizeSha256(value: string | null | undefined) {
  return normalizeOptionalText(value)?.toLowerCase() ?? null;
}

export default function WpfVersionsScreen() {
  const theme = useTheme();
  const { t, language } = useAppTranslation(["wpfVersions", "common"]);
  const isAdmin = useAuthStore((state) => state.access.isAdmin);
  const [query, setQuery] = useState<WpfReleaseQuery>({
    channel: "production",
    includeDisabled: false,
    page: 1,
    pageSize: PAGE_SIZE,
  });
  const [releases, setReleases] = useState<WpfRelease[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState("");
  const [policyReady, setPolicyReady] = useState(false);
  const [activeVersions, setActiveVersions] = useState<string[]>([]);
  const [serverTargetVersion, setServerTargetVersion] = useState<string | null>(
    null,
  );
  const [detail, setDetail] = useState<WpfRelease | null>(null);
  const [editRelease, setEditRelease] = useState<WpfRelease | null>(null);
  const [editDraft, setEditDraft] = useState<{
    downloadUrl: string;
    sha256: string;
    installerType: "exe" | "msi" | null;
    installerArguments: string;
    releaseNotes: string;
  }>({
    downloadUrl: "",
    sha256: "",
    installerType: null,
    installerArguments: "",
    releaseNotes: "",
  });
  const [qrRelease, setQrRelease] = useState<WpfRelease | null>(null);
  const [snackbar, setSnackbar] = useState("");
  const [updatingId, setUpdatingId] = useState("");
  const [policySaving, setPolicySaving] = useState(false);
  const [policyConfirmVisible, setPolicyConfirmVisible] = useState(false);
  const [policy, setPolicy] = useState<WpfReleasePolicyRequest>({
    channel: "production",
    targetVersion: "",
    minimumSupportedVersion: "",
    forceUpdate: false,
    isRollback: false,
    targetScope: "all",
    targetStoreGuids: [],
    targetDeviceRegistrationIds: [],
  });
  const [stores, setStores] = useState<WpfStoreOption[]>([]);
  const [devices, setDevices] = useState<WpfDeviceOption[]>([]);
  const [deviceKeywordDraft, setDeviceKeywordDraft] = useState("");
  const [deviceKeywordApplied, setDeviceKeywordApplied] = useState("");
  const [devicePage, setDevicePage] = useState(1);
  const [deviceTotal, setDeviceTotal] = useState(0);
  const [targetLoading, setTargetLoading] = useState(false);
  const [targetError, setTargetError] = useState("");
  const releasesGuard = useRef(createLatestRequestGuard());
  const policyGuard = useRef(createLatestRequestGuard());
  const targetGuard = useRef(createLatestRequestGuard());
  const storesLoadedRef = useRef(false);
  const devicesLoadedRef = useRef(false);
  const deviceKeywordAppliedRef = useRef("");

  const loadReleases = useCallback(
    async (nextQuery: WpfReleaseQuery = query) => {
      const requestId = releasesGuard.current.next();
      setLoading(true);
      setLoadError("");
      try {
        const result = await getWpfReleases(nextQuery);
        if (!releasesGuard.current.isCurrent(requestId)) return null;
        setReleases(result.items);
        setTotal(result.total);
        return result;
      } catch (error) {
        if (releasesGuard.current.isCurrent(requestId)) {
          setReleases([]);
          setTotal(0);
          setLoadError(getErrorMessage(error, t("loadFailed")));
        }
        return null;
      } finally {
        if (releasesGuard.current.isCurrent(requestId)) setLoading(false);
      }
    },
    [query, t],
  );

  const loadPolicySnapshot = useCallback(
    async (channel: string) => {
      const requestId = policyGuard.current.next();
      setPolicyReady(false);
      try {
        // 策略 lane 独立读取第一页全量视图，避免分页列表或编辑草稿覆盖当前服务器策略。
        const firstPage = await getWpfReleases({
          channel,
          includeDisabled: true,
          page: 1,
          pageSize: 100,
        });
        if (!policyGuard.current.isCurrent(requestId))
          return { ok: false as const, summary: null };
        const allReleases = [...firstPage.items];
        const pageCount = Math.max(
          1,
          Math.ceil(firstPage.total / Math.max(1, firstPage.pageSize)),
        );
        for (let page = 2; page <= pageCount; page += 1) {
          const nextPage = await getWpfReleases({
            channel,
            includeDisabled: true,
            page,
            pageSize: firstPage.pageSize,
          });
          if (!policyGuard.current.isCurrent(requestId))
            return { ok: false as const, summary: null };
          allReleases.push(...nextPage.items);
        }
        const summary = getPolicySummary(allReleases);
        const availableVersions = [
          ...new Set(
            allReleases
              .filter((item) => item.isActive)
              .map((item) => normalizeVersion(item.version))
              .filter(Boolean),
          ),
        ].sort();
        setActiveVersions(availableVersions);
        if (!summary) {
          setServerTargetVersion(null);
          setPolicy((current) => ({
            ...current,
            channel,
            targetVersion: "",
            minimumSupportedVersion: "",
            forceUpdate: false,
            targetScope: "all",
            targetStoreGuids: [],
            targetDeviceRegistrationIds: [],
            isRollback: false,
            rollbackConfirmed: undefined,
          }));
        } else {
          setServerTargetVersion(summary.targetVersion);
          setPolicy((current) => ({
            ...current,
            channel: summary.channel,
            targetVersion: summary.targetVersion,
            minimumSupportedVersion: summary.minimumSupportedVersion,
            forceUpdate: summary.forceUpdate,
            targetScope: summary.targetScope,
            targetStoreGuids: summary.targetStoreGuids,
            targetDeviceRegistrationIds: summary.targetDeviceRegistrationIds,
            isRollback: false,
            rollbackConfirmed: undefined,
          }));
        }
        if (!summary) {
          // 成功读取但尚无策略时允许管理员创建首条策略；保存后的核验仍要求服务端返回完整摘要。
          setPolicyReady(true);
          return { ok: true as const, summary: null };
        }
        setPolicyReady(true);
        return { ok: true as const, summary };
      } catch (error) {
        if (policyGuard.current.isCurrent(requestId)) {
          setPolicyReady(false);
          setSnackbar(getErrorMessage(error, t("policyLoadFailed")));
        }
        return { ok: false as const, summary: null };
      }
    },
    [t],
  );

  useEffect(() => {
    void loadReleases(query);
  }, [loadReleases, query]);

  useEffect(() => {
    setReleases([]);
    setTotal(0);
    setLoadError("");
  }, [query.channel, query.includeDisabled, query.page]);

  useEffect(() => {
    setPolicyReady(false);
    setServerTargetVersion(null);
    setActiveVersions([]);
    setPolicy((current) => ({
      ...current,
      channel: query.channel,
      targetVersion: "",
      minimumSupportedVersion: "",
      forceUpdate: false,
      targetScope: "all",
      targetStoreGuids: [],
      targetDeviceRegistrationIds: [],
      isRollback: false,
      rollbackConfirmed: undefined,
    }));
    void loadPolicySnapshot(query.channel);
  }, [loadPolicySnapshot, query.channel]);

  useEffect(
    () => () => {
      releasesGuard.current.invalidate();
      policyGuard.current.invalidate();
      targetGuard.current.invalidate();
    },
    [],
  );

  const loadStores = useCallback(async () => {
    const requestId = targetGuard.current.next();
    setTargetLoading(true);
    setTargetError("");
    try {
      const result = await getWpfTargetStores();
      if (targetGuard.current.isCurrent(requestId)) {
        setStores(result);
        storesLoadedRef.current = true;
      }
    } catch (error) {
      if (targetGuard.current.isCurrent(requestId))
        setTargetError(getErrorMessage(error, t("targetLoadFailed")));
    } finally {
      if (targetGuard.current.isCurrent(requestId)) setTargetLoading(false);
    }
  }, [t]);

  const loadDevices = useCallback(
    async (
      keyword = deviceKeywordAppliedRef.current,
      nextPage = 1,
      append = false,
    ) => {
      const requestId = targetGuard.current.next();
      setTargetLoading(true);
      setTargetError("");
      try {
        const result = await getWpfTargetDevices({
          keyword,
          page: nextPage,
          pageSize: DEVICE_PAGE_SIZE,
        });
        if (!targetGuard.current.isCurrent(requestId)) return;
        setDevices((current) => {
          const merged = append ? [...current, ...result.items] : result.items;
          return [
            ...new Map(
              merged.map((item) => [item.deviceRegistrationId, item]),
            ).values(),
          ];
        });
        setDevicePage(result.page);
        setDeviceTotal(result.total);
        const appliedKeyword = keyword.trim();
        setDeviceKeywordApplied(appliedKeyword);
        deviceKeywordAppliedRef.current = appliedKeyword;
        devicesLoadedRef.current = true;
      } catch (error) {
        if (targetGuard.current.isCurrent(requestId))
          setTargetError(getErrorMessage(error, t("targetLoadFailed")));
      } finally {
        if (targetGuard.current.isCurrent(requestId)) setTargetLoading(false);
      }
    },
    [t],
  );

  useEffect(() => {
    targetGuard.current.invalidate();
    setTargetError("");
    if (policy.targetScope === "stores" && !storesLoadedRef.current)
      void loadStores();
    if (policy.targetScope === "devices" && !devicesLoadedRef.current)
      void loadDevices("", 1, false);
  }, [loadDevices, loadStores, policy.targetScope]);

  const policyError = getPolicyValidationError({ ...policy, activeVersions });
  const currentTargetVersion = serverTargetVersion;
  const mutationBusy = policySaving || Boolean(updatingId);
  const pages = Math.max(1, Math.ceil(total / query.pageSize));

  const updateQuery = (patch: Partial<WpfReleaseQuery>) =>
    setQuery((current) => ({
      ...current,
      ...patch,
      ...(patch.channel || patch.includeDisabled !== undefined
        ? { page: 1 }
        : {}),
    }));

  const readReleaseById = useCallback(
    async (releaseId: string, channel: string) => {
      let page = 1;
      const pageSize = 100;
      while (true) {
        const result = await getWpfReleases({
          channel,
          includeDisabled: true,
          page,
          pageSize,
        });
        const match = result.items.find((item) => item.id === releaseId);
        if (match) return match;
        const effectivePageSize = Math.max(1, result.pageSize || pageSize);
        if (
          result.items.length === 0 ||
          page * effectivePageSize >= result.total
        )
          break;
        page += 1;
      }
      return null;
    },
    [],
  );

  const openUrl = useCallback(
    async (url: string | null) => {
      if (!url) {
        setSnackbar(t("noDownloadUrl"));
        return;
      }
      try {
        if (!isSafeDownloadUrl(url)) throw new Error("URL_NOT_SUPPORTED");
        if (!(await Linking.canOpenURL(url)))
          throw new Error("URL_NOT_SUPPORTED");
        await Linking.openURL(url);
      } catch (error) {
        setSnackbar(getErrorMessage(error, t("openFailed")));
      }
    },
    [t],
  );

  const copyUrl = useCallback(
    async (url: string | null) => {
      if (!isSafeDownloadUrl(url)) {
        setSnackbar(t("noDownloadUrl"));
        return;
      }
      try {
        await Clipboard.setStringAsync(url);
        setSnackbar(t("copySuccess"));
      } catch (error) {
        setSnackbar(getErrorMessage(error, t("copyFailed")));
      }
    },
    [t],
  );

  const toggleRelease = async (release: WpfRelease) => {
    setUpdatingId(release.id);
    try {
      await updateWpfRelease(release.id, { isActive: !release.isActive });
      // 状态写入完成后独立重新读取，避免把本地乐观状态当作服务器事实。
      let readBack: WpfRelease | null = null;
      let readBackFailed = false;
      try {
        readBack = await readReleaseById(release.id, query.channel);
      } catch {
        readBackFailed = true;
      }
      const refreshed = await loadReleases(query);
      let policyReadBack: { ok: boolean; summary: WpfPolicySummary | null } = {
        ok: false,
        summary: null,
      };
      try {
        policyReadBack = await loadPolicySnapshot(query.channel);
      } catch {
        readBackFailed = true;
      }
      if (
        readBackFailed ||
        readBack === null ||
        refreshed === null ||
        !policyReadBack.ok ||
        readBack.isActive !== !release.isActive
      ) {
        setSnackbar(t("savedButReadFailed"));
      } else {
        setSnackbar(
          release.isActive ? t("disabledSuccess") : t("enabledSuccess"),
        );
      }
    } catch (error) {
      setSnackbar(getErrorMessage(error, t("saveFailed")));
    } finally {
      setUpdatingId("");
    }
  };

  const openEditor = (release: WpfRelease) => {
    setEditRelease(release);
    setEditDraft({
      downloadUrl: release.downloadUrl ?? "",
      sha256: release.sha256 ?? "",
      installerType: release.installerType,
      installerArguments: release.installerArguments ?? "",
      releaseNotes: release.releaseNotes ?? "",
    });
  };

  const saveReleaseMetadata = async () => {
    if (!editRelease) return;
    if (
      editDraft.downloadUrl.trim() &&
      !isSafeDownloadUrl(editDraft.downloadUrl)
    ) {
      setSnackbar(t("invalidUrl"));
      return;
    }
    if (!isValidSha256(editDraft.sha256)) {
      setSnackbar(t("invalidSha256"));
      return;
    }
    setUpdatingId(editRelease.id);
    try {
      await updateWpfRelease(editRelease.id, editDraft);
      setEditRelease(null);
      let readBack: WpfRelease | null = null;
      let readBackFailed = false;
      try {
        readBack = await readReleaseById(editRelease.id, query.channel);
      } catch {
        readBackFailed = true;
      }
      const refreshed = await loadReleases(query);
      const metadataMatches =
        readBack !== null &&
        normalizeOptionalText(readBack.downloadUrl) ===
          normalizeOptionalText(editDraft.downloadUrl) &&
        normalizeSha256(readBack.sha256) ===
          normalizeSha256(editDraft.sha256) &&
        readBack.installerType === editDraft.installerType &&
        normalizeOptionalText(readBack.installerArguments) ===
          normalizeOptionalText(editDraft.installerArguments) &&
        normalizeOptionalText(readBack.releaseNotes) ===
          normalizeOptionalText(editDraft.releaseNotes);
      setSnackbar(
        !readBackFailed && refreshed !== null && metadataMatches
          ? t("metadataSaved")
          : t("savedButReadFailed"),
      );
    } catch (error) {
      setSnackbar(getErrorMessage(error, t("saveFailed")));
    } finally {
      setUpdatingId("");
    }
  };

  const performSavePolicy = async (rollbackConfirmed: boolean) => {
    if (!policyReady || !canSavePolicy({ ...policy, activeVersions })) return;
    setPolicySaving(true);
    const targetVersion = normalizeVersion(policy.targetVersion);
    const payload: WpfReleasePolicyRequest = {
      ...policy,
      channel: query.channel,
      targetVersion,
      minimumSupportedVersion: normalizeVersion(policy.minimumSupportedVersion),
      isRollback: inferRollback(targetVersion, currentTargetVersion),
      rollbackConfirmed: rollbackConfirmed || undefined,
    };
    try {
      await saveWpfPolicy(payload);
      setPolicy((current) => ({
        ...current,
        isRollback: false,
        rollbackConfirmed: undefined,
      }));
      // 策略保存后必须重新请求版本与策略摘要，独立验证服务端最终状态。
      const verifiedList = await loadReleases(query);
      const verifiedPolicy = await loadPolicySnapshot(query.channel);
      if (
        verifiedList === null ||
        !verifiedPolicy.ok ||
        !verifiedPolicy.summary ||
        !policySummaryMatchesRequest(payload, verifiedPolicy.summary)
      ) {
        setSnackbar(t("savedButReadFailed"));
        return;
      }
      setSnackbar(t("policyVerified"));
    } catch (error) {
      setSnackbar(getErrorMessage(error, t("saveFailed")));
    } finally {
      setPolicySaving(false);
    }
  };

  const savePolicy = () => {
    if (!policyReady || !canSavePolicy({ ...policy, activeVersions })) return;
    setPolicyConfirmVisible(true);
  };

  const policyIsRollback = inferRollback(
    policy.targetVersion,
    currentTargetVersion,
  );
  const selectedStoreLabels = policy.targetStoreGuids.map((storeGuid) => {
    const store = stores.find((item) => item.storeGuid === storeGuid);
    return store
      ? [store.storeCode, store.storeName].filter(Boolean).join(" · ") ||
          store.storeGuid
      : storeGuid;
  });
  const selectedDeviceLabels = policy.targetDeviceRegistrationIds.map(
    (deviceId) => {
      const device = devices.find(
        (item) => item.deviceRegistrationId === deviceId,
      );
      return device ? deviceLabel(device) : `#${deviceId}`;
    },
  );

  if (!isAdmin) {
    return (
      <SafeAreaView style={styles.center}>
        <Text>{t("adminOnly")}</Text>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.safe}>
      <ScrollView
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
      >
        <View style={styles.headerRow}>
          <View style={styles.flex}>
            <Text variant="headlineSmall">{t("title")}</Text>
            <Text variant="bodySmall" style={styles.muted}>
              {t("subtitle")}
            </Text>
          </View>
          <IconButton
            icon="refresh"
            accessibilityLabel={t("refresh")}
            onPress={() => void loadReleases(query)}
            disabled={loading}
          />
        </View>

        <Card mode="contained" style={styles.card}>
          <Card.Content>
            <Text variant="titleMedium">{t("filters")}</Text>
            <SegmentedButtons
              value={query.channel}
              onValueChange={(value) => updateQuery({ channel: value })}
              buttons={[
                {
                  value: "production",
                  label: t("production"),
                  disabled: mutationBusy,
                },
                {
                  value: "preview",
                  label: t("preview"),
                  disabled: mutationBusy,
                },
              ]}
              style={styles.segmented}
            />
            <View style={styles.switchRow}>
              <Text>{t("includeDisabled")}</Text>
              <Switch
                value={query.includeDisabled}
                onValueChange={(value) =>
                  updateQuery({ includeDisabled: value })
                }
                disabled={mutationBusy}
              />
            </View>
            <Button
              mode="text"
              icon="open-in-new"
              onPress={() => void openUrl(WEB_WPF_VERSIONS_URL)}
            >
              {t("openWeb")}
            </Button>
          </Card.Content>
        </Card>

        <Card mode="contained" style={styles.card}>
          <Card.Content>
            <Text variant="titleMedium">{t("policyTitle")}</Text>
            <TextInput
              label={t("targetVersion")}
              value={policy.targetVersion}
              onChangeText={(value) =>
                setPolicy((current) => ({ ...current, targetVersion: value }))
              }
              mode="outlined"
              style={styles.input}
            />
            <TextInput
              label={t("minimumVersion")}
              value={policy.minimumSupportedVersion}
              onChangeText={(value) =>
                setPolicy((current) => ({
                  ...current,
                  minimumSupportedVersion: value,
                }))
              }
              mode="outlined"
              style={styles.input}
            />
            {policyError ? (
              <HelperText type="error">
                {t(`validation.${policyError}`)}
              </HelperText>
            ) : null}
            <View style={styles.switchRow}>
              <Text>{t("forceUpdate")}</Text>
              <Switch
                value={policy.forceUpdate}
                onValueChange={(value) =>
                  setPolicy((current) => ({ ...current, forceUpdate: value }))
                }
              />
            </View>
            <Text variant="labelLarge" style={styles.label}>
              {t("targetScope")}
            </Text>
            <SegmentedButtons
              value={normalizeTargetScope(policy.targetScope)}
              onValueChange={(value) =>
                setPolicy((current) => ({
                  ...current,
                  targetScope: normalizeTargetScope(value),
                  targetStoreGuids: [],
                  targetDeviceRegistrationIds: [],
                }))
              }
              buttons={[
                {
                  value: "all",
                  label: t("allTargets"),
                  disabled: mutationBusy || policyConfirmVisible,
                },
                {
                  value: "stores",
                  label: t("stores"),
                  disabled: mutationBusy || policyConfirmVisible,
                },
                {
                  value: "devices",
                  label: t("devices"),
                  disabled: mutationBusy || policyConfirmVisible,
                },
              ]}
              style={styles.segmented}
            />
            {targetError ? (
              <HelperText type="error">{targetError}</HelperText>
            ) : null}
            {policy.targetScope === "stores" ? (
              <View style={styles.targetList}>
                {targetLoading && stores.length === 0 ? (
                  <ActivityIndicator />
                ) : (
                  stores.map((store) => (
                    <Checkbox.Item
                      key={store.storeGuid}
                      label={
                        [store.storeCode, store.storeName]
                          .filter(Boolean)
                          .join(" · ") || store.storeGuid
                      }
                      status={
                        policy.targetStoreGuids.includes(store.storeGuid)
                          ? "checked"
                          : "unchecked"
                      }
                      onPress={() =>
                        setPolicy((current) => ({
                          ...current,
                          targetStoreGuids: current.targetStoreGuids.includes(
                            store.storeGuid,
                          )
                            ? current.targetStoreGuids.filter(
                                (value) => value !== store.storeGuid,
                              )
                            : [...current.targetStoreGuids, store.storeGuid],
                        }))
                      }
                    />
                  ))
                )}
              </View>
            ) : null}
            {policy.targetScope === "devices" ? (
              <View style={styles.targetList}>
                <Searchbar
                  value={deviceKeywordDraft}
                  onChangeText={setDeviceKeywordDraft}
                  onSubmitEditing={() =>
                    !targetLoading &&
                    void loadDevices(deviceKeywordDraft, 1, false)
                  }
                  placeholder={t("searchDevices")}
                />
                <View style={styles.searchActions}>
                  <Button
                    mode="text"
                    onPress={() =>
                      void loadDevices(deviceKeywordDraft, 1, false)
                    }
                    disabled={targetLoading}
                  >
                    {t("search")}
                  </Button>
                  {deviceTotal > devices.length ? (
                    <Button
                      mode="text"
                      onPress={() =>
                        void loadDevices(
                          deviceKeywordApplied,
                          devicePage + 1,
                          true,
                        )
                      }
                      loading={targetLoading}
                      disabled={targetLoading}
                    >
                      {t("loadMore")}
                    </Button>
                  ) : null}
                </View>
                {devices.map((device) => (
                  <Checkbox.Item
                    key={device.deviceRegistrationId}
                    label={deviceLabel(device)}
                    status={
                      policy.targetDeviceRegistrationIds.includes(
                        device.deviceRegistrationId,
                      )
                        ? "checked"
                        : "unchecked"
                    }
                    onPress={() =>
                      setPolicy((current) => ({
                        ...current,
                        targetDeviceRegistrationIds:
                          current.targetDeviceRegistrationIds.includes(
                            device.deviceRegistrationId,
                          )
                            ? current.targetDeviceRegistrationIds.filter(
                                (value) =>
                                  value !== device.deviceRegistrationId,
                              )
                            : [
                                ...current.targetDeviceRegistrationIds,
                                device.deviceRegistrationId,
                              ],
                      }))
                    }
                  />
                ))}
              </View>
            ) : null}
            <Button
              mode="contained"
              icon="content-save"
              onPress={savePolicy}
              disabled={
                mutationBusy || !policyReady || Boolean(policyError) || loading
              }
              loading={policySaving}
              style={styles.actionButton}
            >
              {t("savePolicy")}
            </Button>
          </Card.Content>
        </Card>

        <View style={styles.listHeader}>
          <Text variant="titleMedium">{t("releaseList")}</Text>
          <Text variant="bodySmall" style={styles.muted}>
            {t("count", { count: total })}
          </Text>
        </View>
        {loading && releases.length === 0 ? (
          <ActivityIndicator style={styles.loader} />
        ) : null}
        {loadError ? <HelperText type="error">{loadError}</HelperText> : null}
        {!loading && releases.length === 0 && !loadError ? (
          <Text style={styles.muted}>{t("empty")}</Text>
        ) : null}
        {releases.map((release) => (
          <Card
            key={
              release.id ||
              `${release.channel}-${release.version}-${release.fileName}`
            }
            mode="contained"
            style={styles.card}
          >
            <Card.Content>
              <View style={styles.releaseTop}>
                <View style={styles.flex}>
                  <Text variant="titleMedium">{release.version}</Text>
                  <Text variant="bodySmall" style={styles.muted}>
                    {release.fileName || "-"} ·{" "}
                    {formatFileSize(release.fileSize)}
                  </Text>
                </View>
                <Switch
                  value={release.isActive}
                  onValueChange={() => void toggleRelease(release)}
                  disabled={mutationBusy || updatingId === release.id}
                />
              </View>
              <View style={styles.badgeRow}>
                {release.isCurrent ? (
                  <Surface
                    style={[
                      styles.badge,
                      { backgroundColor: theme.colors.primaryContainer },
                    ]}
                  >
                    <Text variant="labelSmall">{t("current")}</Text>
                  </Surface>
                ) : null}
                {release.forceUpdate ? (
                  <Surface
                    style={[
                      styles.badge,
                      { backgroundColor: theme.colors.errorContainer },
                    ]}
                  >
                    <Text variant="labelSmall">{t("forced")}</Text>
                  </Surface>
                ) : null}
                <Text variant="bodySmall" style={styles.muted}>
                  {release.installerType?.toUpperCase() || "-"} · SHA{" "}
                  {maskSha256(release.sha256)}
                </Text>
              </View>
              <Text variant="bodySmall" style={styles.muted}>
                {t("updatedAt")}:{" "}
                {dateText(
                  release.updatedAt || release.createdAt,
                  language === "zh" ? "zh-CN" : "en-AU",
                )}
              </Text>
              <View style={styles.buttonRow}>
                <Button
                  compact
                  mode="text"
                  onPress={() => setDetail(release)}
                  disabled={mutationBusy}
                >
                  {t("details")}
                </Button>
                <Button
                  compact
                  mode="text"
                  icon="pencil"
                  onPress={() => openEditor(release)}
                  disabled={mutationBusy}
                >
                  {t("edit")}
                </Button>
                <Button
                  compact
                  mode="text"
                  icon="open-in-new"
                  onPress={() => void openUrl(release.downloadUrl)}
                  disabled={
                    !isSafeDownloadUrl(release.downloadUrl) || mutationBusy
                  }
                >
                  {t("open")}
                </Button>
                <Button
                  compact
                  mode="text"
                  icon="content-copy"
                  onPress={() => void copyUrl(release.downloadUrl)}
                  disabled={
                    !isSafeDownloadUrl(release.downloadUrl) || mutationBusy
                  }
                >
                  {t("copy")}
                </Button>
                <Button
                  compact
                  mode="text"
                  icon="qrcode"
                  onPress={() => setQrRelease(release)}
                  disabled={
                    !isSafeDownloadUrl(release.downloadUrl) || mutationBusy
                  }
                >
                  {t("qrCode")}
                </Button>
              </View>
            </Card.Content>
          </Card>
        ))}
        <View style={styles.pagination}>
          <Button
            mode="outlined"
            onPress={() => updateQuery({ page: Math.max(1, query.page - 1) })}
            disabled={query.page <= 1 || loading}
          >
            {t("previous")}
          </Button>
          <Text>{t("page", { page: query.page, total: pages })}</Text>
          <Button
            mode="outlined"
            onPress={() =>
              updateQuery({ page: Math.min(pages, query.page + 1) })
            }
            disabled={query.page >= pages || loading}
          >
            {t("next")}
          </Button>
        </View>
      </ScrollView>

      <Portal>
        <Modal
          visible={Boolean(detail)}
          onDismiss={() => setDetail(null)}
          contentContainerStyle={styles.modal}
        >
          <View style={styles.modalHeader}>
            <Text variant="titleLarge">{detail?.version || t("details")}</Text>
            <IconButton icon="close" onPress={() => setDetail(null)} />
          </View>
          <Divider />
          {detail ? (
            <ScrollView>
              {(
                [
                  [t("channel"), detail.channel],
                  [t("fileName"), detail.fileName],
                  [t("fileSize"), formatFileSize(detail.fileSize)],
                  [t("installerArguments"), detail.installerArguments || "-"],
                  [t("releaseNotes"), detail.releaseNotes || "-"],
                  [t("downloadUrl"), detail.downloadUrl || "-"],
                  [
                    t("createdAt"),
                    dateText(
                      detail.createdAt,
                      language === "zh" ? "zh-CN" : "en-AU",
                    ),
                  ],
                  [
                    t("updatedAt"),
                    dateText(
                      detail.updatedAt,
                      language === "zh" ? "zh-CN" : "en-AU",
                    ),
                  ],
                ] as DetailField[]
              ).map(([label, value]) => (
                <View key={label} style={styles.detailRow}>
                  <Text variant="labelMedium" style={styles.detailLabel}>
                    {label}
                  </Text>
                  <Text selectable>{value}</Text>
                </View>
              ))}
            </ScrollView>
          ) : null}
        </Modal>
      </Portal>
      <Portal>
        <Modal
          visible={Boolean(qrRelease)}
          onDismiss={() => setQrRelease(null)}
          contentContainerStyle={styles.qrModal}
        >
          <Text variant="titleLarge">{qrRelease?.version || t("qrCode")}</Text>
          {qrRelease?.downloadUrl ? (
            <QRCode
              value={qrRelease.downloadUrl}
              size={220}
              backgroundColor="white"
              color="black"
            />
          ) : null}
          <Text selectable style={styles.qrUrl}>
            {qrRelease?.downloadUrl || ""}
          </Text>
          <Button
            mode="contained"
            icon="content-copy"
            onPress={() => void copyUrl(qrRelease?.downloadUrl ?? null)}
          >
            {t("copy")}
          </Button>
        </Modal>
      </Portal>
      <Portal>
        <Modal
          visible={Boolean(editRelease)}
          onDismiss={() => setEditRelease(null)}
          contentContainerStyle={styles.modal}
        >
          <View style={styles.modalHeader}>
            <Text variant="titleLarge">{t("editMetadata")}</Text>
            <IconButton icon="close" onPress={() => setEditRelease(null)} />
          </View>
          <Divider />
          <TextInput
            label={t("downloadUrl")}
            value={editDraft.downloadUrl}
            onChangeText={(value) =>
              setEditDraft((current) => ({ ...current, downloadUrl: value }))
            }
            mode="outlined"
            style={styles.input}
            autoCapitalize="none"
          />
          <TextInput
            label={t("sha256")}
            value={editDraft.sha256}
            onChangeText={(value) =>
              setEditDraft((current) => ({ ...current, sha256: value }))
            }
            mode="outlined"
            style={styles.input}
            autoCapitalize="none"
          />
          <Text variant="labelLarge" style={styles.label}>
            {t("installerType")}
          </Text>
          <SegmentedButtons
            value={editDraft.installerType ?? "exe"}
            onValueChange={(value) =>
              setEditDraft((current) => ({
                ...current,
                installerType: value === "msi" ? "msi" : "exe",
              }))
            }
            buttons={[
              { value: "exe", label: "EXE" },
              { value: "msi", label: "MSI" },
            ]}
            style={styles.segmented}
          />
          <TextInput
            label={t("installerArguments")}
            value={editDraft.installerArguments}
            onChangeText={(value) =>
              setEditDraft((current) => ({
                ...current,
                installerArguments: value,
              }))
            }
            mode="outlined"
            style={styles.input}
          />
          <TextInput
            label={t("releaseNotes")}
            value={editDraft.releaseNotes}
            onChangeText={(value) =>
              setEditDraft((current) => ({ ...current, releaseNotes: value }))
            }
            mode="outlined"
            multiline
            style={styles.input}
          />
          <Button
            mode="contained"
            icon="content-save"
            onPress={() => void saveReleaseMetadata()}
            loading={Boolean(updatingId)}
            disabled={Boolean(updatingId)}
            style={styles.actionButton}
          >
            {t("saveMetadata")}
          </Button>
        </Modal>
      </Portal>
      <Portal>
        <Modal
          visible={policyConfirmVisible}
          onDismiss={() => setPolicyConfirmVisible(false)}
          contentContainerStyle={styles.modal}
        >
          <View style={styles.modalHeader}>
            <Text variant="titleLarge">
              {policyIsRollback ? t("rollbackTitle") : t("confirmPolicyTitle")}
            </Text>
            <IconButton
              icon="close"
              onPress={() => setPolicyConfirmVisible(false)}
            />
          </View>
          <Divider />
          <Text style={styles.confirmText}>
            {policyIsRollback
              ? t("rollbackMessage")
              : t("confirmPolicyMessage")}
          </Text>
          <View style={styles.confirmSummary}>
            <Text>
              <Text variant="labelLarge">{t("targetVersion")}: </Text>
              {normalizeVersion(policy.targetVersion)}
            </Text>
            <Text>
              <Text variant="labelLarge">{t("minimumVersion")}: </Text>
              {normalizeVersion(policy.minimumSupportedVersion)}
            </Text>
            <Text>
              <Text variant="labelLarge">{t("forceUpdate")}: </Text>
              {policy.forceUpdate ? t("yes") : t("no")}
            </Text>
            <Text>
              <Text variant="labelLarge">{t("targetScope")}: </Text>
              {t(
                policy.targetScope === "stores"
                  ? "stores"
                  : policy.targetScope === "devices"
                    ? "devices"
                    : "allTargets",
              )}
            </Text>
            {policy.targetScope === "stores" ? (
              <Text>
                {selectedStoreLabels.length
                  ? selectedStoreLabels.join(", ")
                  : policy.targetStoreGuids.join(", ")}
              </Text>
            ) : null}
            {policy.targetScope === "devices" ? (
              <Text>
                {selectedDeviceLabels.length
                  ? selectedDeviceLabels.join(", ")
                  : policy.targetDeviceRegistrationIds.map(String).join(", ")}
              </Text>
            ) : null}
          </View>
          <View style={styles.confirmButtons}>
            <Button
              mode="outlined"
              onPress={() => setPolicyConfirmVisible(false)}
            >
              {t("cancel")}
            </Button>
            <Button
              mode="contained"
              onPress={() => {
                setPolicyConfirmVisible(false);
                void performSavePolicy(policyIsRollback);
              }}
              loading={policySaving}
            >
              {policyIsRollback ? t("confirmRollback") : t("confirmSave")}
            </Button>
          </View>
        </Modal>
      </Portal>
      <Snackbar
        visible={Boolean(snackbar)}
        onDismiss={() => setSnackbar("")}
        duration={3500}
      >
        {snackbar}
      </Snackbar>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: "#f7f8fa" },
  center: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    padding: 24,
  },
  content: { padding: 16, paddingBottom: 44, gap: 12 },
  headerRow: { flexDirection: "row", alignItems: "center", gap: 8 },
  flex: { flex: 1 },
  muted: { opacity: 0.68 },
  card: { marginBottom: 2 },
  segmented: { marginTop: 10, marginBottom: 10 },
  switchRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    minHeight: 48,
  },
  input: { marginTop: 10 },
  label: { marginTop: 8 },
  targetList: { marginTop: 8, borderRadius: 8, overflow: "hidden" },
  searchActions: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  actionButton: { marginTop: 12 },
  listHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    marginTop: 6,
  },
  loader: { marginVertical: 24 },
  releaseTop: { flexDirection: "row", alignItems: "center" },
  badgeRow: {
    flexDirection: "row",
    alignItems: "center",
    flexWrap: "wrap",
    gap: 8,
    marginTop: 8,
    marginBottom: 4,
  },
  badge: { borderRadius: 4, paddingHorizontal: 8, paddingVertical: 3 },
  buttonRow: { flexDirection: "row", flexWrap: "wrap", gap: 2, marginTop: 6 },
  pagination: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginTop: 4,
  },
  modal: {
    margin: 20,
    padding: 18,
    backgroundColor: "white",
    borderRadius: 12,
    maxHeight: "80%",
  },
  modalHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  detailRow: { paddingVertical: 10, gap: 4 },
  detailLabel: { opacity: 0.7 },
  qrModal: {
    margin: 20,
    padding: 24,
    alignItems: "center",
    gap: 16,
    backgroundColor: "white",
    borderRadius: 12,
  },
  qrUrl: { textAlign: "center", opacity: 0.72 },
  confirmText: { marginTop: 14 },
  confirmSummary: {
    gap: 8,
    marginTop: 16,
    padding: 12,
    borderRadius: 8,
    backgroundColor: "#f5f6f8",
  },
  confirmButtons: {
    flexDirection: "row",
    justifyContent: "flex-end",
    gap: 8,
    marginTop: 18,
  },
});
