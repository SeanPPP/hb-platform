import { useRef, useState, type ReactNode } from "react";
import { ActivityIndicator, Linking, StyleSheet, View } from "react-native";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { IconButton, Portal, Snackbar, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { apiClient } from "@/shared/api/client";
import { BUSINESS_UI } from "@/components/ui/business-ui";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import { appDownloadsApi } from "./api";
import { useLocalCopy, type SaveResult } from "./copy";
import {
  buildNativePolicyPayload,
  buildOtaPolicyPayload,
  isPolicyConflict,
  resolveAppDownloadsSection,
  type AppDownloadsApp,
  type AppDownloadsChannel,
} from "./logic";
import { NativeChannel } from "./native-channel";
import { OtaChannel } from "./ota-channel";
import {
  loadSectionData,
  sectionQueryKey,
  type SectionData,
} from "./section-data";
import type {
  AppDownloadEnvironment,
  AppDownloadPlatform,
  AppUpdateApp,
  HandheldPolicy,
  NativePolicyForm,
  OtaPolicyForm,
} from "./types";
import {
  Panel,
  ScreenFrame,
  SegmentedControl,
  TextLink,
  UnderlineTabs,
  ui,
} from "./ui";

function resolveWebAppDownloadsUrl() {
  const baseUrl = apiClient.defaults.baseURL;
  try {
    return new URL("/system/app-downloads", baseUrl).toString();
  } catch {
    return null;
  }
}

export default function AppDownloadsScreen() {
  const copy = useLocalCopy();
  const queryClient = useQueryClient();
  const [app, setApp] = useState<AppDownloadsApp>("mobile");
  const [channel, setChannel] = useState<AppDownloadsChannel>("native");
  const [profile, setProfile] = useState<AppDownloadEnvironment>("production");
  const [platform, setPlatform] = useState<AppDownloadPlatform>("ios");
  const [saving, setSaving] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [snack, setSnack] = useState("");
  const saveInFlightRef = useRef(false);
  const section = resolveAppDownloadsSection(app, channel);
  const query = useQuery<SectionData>({
    queryKey: sectionQueryKey(section, profile, platform),
    queryFn: () => loadSectionData(section, profile, platform),
    retry: 1,
  });
  const data = query.data;
  const apiBaseUrl = apiClient.defaults.baseURL;
  const webAppDownloadsUrl = resolveWebAppDownloadsUrl();

  const readBack = async () => {
    try {
      const result = await query.refetch({ throwOnError: true });
      if (!result.data) throw new Error(copy.readbackFailed);
    } catch (error) {
      if (error instanceof Error && error.message === copy.readbackFailed)
        throw error;
      const wrapped = new Error(copy.readbackFailed);
      Object.assign(wrapped, { cause: error });
      throw wrapped;
    }
  };

  /**
   * 所有策略写入的统一出口：同一时刻只允许一个写入；
   * 版本冲突时重新读取权威状态，编辑弹层据此重置表单并提示管理员复核。
   */
  const runSave = async (
    execute: () => Promise<void>,
    successMessage = copy.saved,
  ): Promise<SaveResult> => {
    if (saveInFlightRef.current) return { ok: false, message: "" };
    saveInFlightRef.current = true;
    setSaving(true);
    try {
      await execute();
      await readBack();
      setSnack(successMessage);
      return { ok: true, message: successMessage };
    } catch (error) {
      if (isPolicyConflict(error)) {
        try {
          await readBack();
          return { ok: false, message: copy.conflict };
        } catch {
          return {
            ok: false,
            message: `${copy.conflict} · ${copy.readbackFailed}`,
          };
        }
      }
      // 写入已成功、只是读回失败：关闭弹层，避免重复提交
      if (error instanceof Error && error.message === copy.readbackFailed) {
        setSnack(copy.readbackFailed);
        return { ok: true, message: copy.readbackFailed };
      }
      return { ok: false, message: `${copy.error}: ${String(error)}` };
    } finally {
      saveInFlightRef.current = false;
      setSaving(false);
    }
  };

  const register = (
    target: AppUpdateApp,
    value: { appStoreId: string; buildNumber: string; storefront: string },
  ) =>
    runSave(async () => {
      await appDownloadsApi.registerIosRelease({
        app: target,
        appStoreId: value.appStoreId.trim(),
        buildNumber: value.buildNumber.trim(),
        storefront: value.storefront.trim().toLowerCase(),
      });
    }, copy.registered);

  const saveNative = (form: NativePolicyForm) =>
    runSave(async () => {
      if (data?.kind !== "native") throw new Error(copy.error);
      await appDownloadsApi.saveNativePolicy(
        data.app,
        buildNativePolicyPayload(
          form,
          data.policy.policyVersion,
          data.app === "pos-ipad",
        ),
      );
    });

  const saveMobileOta = (form: OtaPolicyForm) =>
    runSave(async () => {
      if (data?.kind !== "mobile-ota") throw new Error(copy.error);
      await appDownloadsApi.saveMobileOtaPolicy(
        data.policy.environment,
        data.policy.platform,
        buildOtaPolicyPayload(form, data.policy.policyVersion),
      );
    });

  const saveIpadOta = (form: OtaPolicyForm) =>
    runSave(async () => {
      if (data?.kind !== "ipad-ota") throw new Error(copy.error);
      const stores = form.enabled && form.targetScope === "stores";
      await appDownloadsApi.saveIpadOtaRollout({
        expectedPolicyVersion: data.rollout.policyVersion,
        enabled: form.enabled,
        releaseId: form.enabled ? form.targetReleaseId || null : null,
        forceUpdate: form.enabled && form.required,
        targetScope: stores ? "stores" : "all",
        targetStoreGuids: stores ? (form.targetStoreGuids ?? []) : [],
        releaseMessage: form.enabled
          ? form.releaseMessage.trim() || null
          : null,
      });
    });

  const saveHandheld = (policy: HandheldPolicy, form: NativePolicyForm) =>
    runSave(async () => {
      const native = policy.lane.endsWith("native");
      await appDownloadsApi.saveHandheldPolicy(policy.lane, {
        expectedPolicyVersion: policy.policyVersion,
        enabled: form.enabled,
        required: form.enabled && form.required === true,
        candidateId: form.enabled ? form.releaseId.trim() || null : null,
        minimumSupportedVersion:
          form.enabled && native
            ? form.minimumSupportedVersion.trim() || null
            : null,
        minimumSupportedBuildNumber:
          form.enabled && native && form.minimumSupportedBuildNumber.trim()
            ? Number(form.minimumSupportedBuildNumber)
            : null,
        releaseMessage: form.enabled
          ? form.releaseMessage.trim() || null
          : null,
      });
    });

  const refresh = async () => {
    setRefreshing(true);
    try {
      await Promise.all([
        query.refetch(),
        queryClient.invalidateQueries({
          queryKey: ["app-downloads-build-latest"],
        }),
      ]);
    } finally {
      setRefreshing(false);
    }
  };
  const onRefresh = () => void refresh();

  const header = (
    <View style={styles.header}>
      <View style={styles.titleRow}>
        <View style={ui.flexText}>
          <Text accessibilityRole="header" style={styles.title}>
            {copy.title}
          </Text>
          <Text style={styles.subtitle}>{copy.subtitle}</Text>
        </View>
        {webAppDownloadsUrl ? (
          <IconButton
            icon="web"
            mode="outlined"
            accessibilityLabel={copy.openWeb}
            iconColor={HB_COLORS.action}
            size={20}
            style={styles.webButton}
            onPress={() =>
              void Linking.openURL(webAppDownloadsUrl).catch(() =>
                setSnack(copy.error),
              )
            }
          />
        ) : null}
      </View>
      <SegmentedControl
        tone="strong"
        options={[
          { value: "mobile", label: copy.apps.mobile },
          { value: "ipad", label: copy.apps.ipad },
          { value: "handheld", label: copy.apps.handheld },
        ]}
        value={app}
        onChange={setApp}
        disabled={saving}
      />
      <UnderlineTabs
        options={[
          { value: "native", label: copy.channels.native },
          { value: "ota", label: copy.channels.ota },
        ]}
        value={channel}
        onChange={setChannel}
        disabled={saving}
      />
    </View>
  );

  let content: ReactNode;
  if (query.isLoading || !data) {
    content = (
      <ScreenFrame header={header} refreshing={false} onRefresh={onRefresh}>
        {query.error ? (
          <Panel>
            <View style={ui.cardBody}>
              <Text style={ui.error}>
                {copy.loadFailed}: {String(query.error)}
              </Text>
              <TextLink
                icon="refresh"
                label={copy.actions.retry}
                onPress={() => void query.refetch()}
              />
            </View>
          </Panel>
        ) : (
          <ActivityIndicator color={HB_COLORS.action} style={styles.loading} />
        )}
      </ScreenFrame>
    );
  } else if (
    channel === "native" &&
    (data.kind === "native" || data.kind === "handheld")
  ) {
    content = (
      <NativeChannel
        header={header}
        app={app}
        data={data}
        profile={profile}
        onProfileChange={setProfile}
        apiBaseUrl={apiBaseUrl}
        copy={copy}
        notify={setSnack}
        saving={saving}
        refreshing={refreshing}
        onRefresh={onRefresh}
        onSaveNative={saveNative}
        onSaveHandheld={saveHandheld}
        onRegister={register}
      />
    );
  } else if (channel === "ota" && data.kind !== "native") {
    content = (
      <OtaChannel
        header={header}
        app={app}
        data={data}
        profile={profile}
        onProfileChange={setProfile}
        platform={platform}
        onPlatformChange={setPlatform}
        copy={copy}
        saving={saving}
        refreshing={refreshing}
        onRefresh={onRefresh}
        onSaveMobile={saveMobileOta}
        onSaveIpad={saveIpadOta}
        onSaveHandheld={saveHandheld}
      />
    );
  } else {
    content = (
      <ScreenFrame header={header} refreshing={false} onRefresh={onRefresh}>
        <ActivityIndicator color={HB_COLORS.action} style={styles.loading} />
      </ScreenFrame>
    );
  }

  return (
    // 顶部避开状态栏与灵动岛；底部由标签栏处理
    <SafeAreaView style={styles.container} edges={["top", "left", "right"]}>
      {content}
      <Portal>
        <Snackbar visible={Boolean(snack)} onDismiss={() => setSnack("")}>
          {snack}
        </Snackbar>
      </Portal>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { ...BUSINESS_UI.screen },
  header: {
    paddingHorizontal: HB_SPACING.md,
    paddingTop: HB_SPACING.xs,
    gap: HB_SPACING.sm,
  },
  titleRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: HB_SPACING.sm,
  },
  title: {
    fontSize: 22,
    lineHeight: 30,
    fontWeight: "700",
    color: HB_COLORS.textPrimary,
  },
  subtitle: { fontSize: 13, lineHeight: 18, color: HB_COLORS.textSecondary },
  webButton: {
    margin: 0,
    width: 44,
    height: 44,
    borderRadius: HB_RADIUS.control,
    borderColor: HB_COLORS.outline,
    backgroundColor: HB_COLORS.white,
  },
  loading: { marginVertical: HB_SPACING.xl },
});
