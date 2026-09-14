import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Linking, ScrollView, Share, StyleSheet, View } from "react-native";
import * as Clipboard from "expo-clipboard";
import { useQuery } from "@tanstack/react-query";
import {
  ActivityIndicator,
  Button,
  Card,
  Chip,
  Modal,
  Portal,
  SegmentedButtons,
  Snackbar,
  Switch,
  Text,
  TextInput,
} from "react-native-paper";
import QRCode from "react-native-qrcode-svg";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { BUSINESS_UI } from "@/components/ui/business-ui";
import { HB_COLORS } from "@/shared/theme/tokens";
import { apiClient } from "@/shared/api/client";
import en from "@/locales/en/appDownloads.json";
import zh from "@/locales/zh/appDownloads.json";
import { appDownloadsApi } from "./api";
import {
  buildConfirmationSummary,
  buildNativePolicyPayload,
  buildOtaPolicyPayload,
  isPolicyConflict,
  mergeHandheldPolicyCandidates,
  validateHandheldPolicy,
  validateNativePolicy,
  validateOtaPolicy,
  validateRegistration,
  validateTargetScope,
  type ConfirmationLabels,
  type PolicyValidationError,
  type RegistrationValidationError,
} from "./logic";
import type {
  AppBuild,
  AppDownloadEnvironment,
  AppDownloadPlatform,
  AppDownloadsSection,
  AppUpdateApp,
  HandheldCandidate,
  HandheldPolicy,
  IpadOtaRelease,
  IpadOtaRollout,
  NativePolicy,
  NativePolicyForm,
  NativeRelease,
  OtaPolicy,
  OtaPolicyForm,
  OtaRelease,
  Revision,
  StoreOption,
} from "./types";
type Copy = typeof zh;
type SectionData =
  | {
      kind: "native";
      app: "mobile-ios" | "pos-ipad";
      releases: NativeRelease[];
      policy: NativePolicy;
      stores: StoreOption[];
    }
  | {
      kind: "mobile-ota";
      releases: OtaRelease[];
      policy: OtaPolicy;
      revisions: Revision[];
    }
  | {
      kind: "ipad-ota";
      releases: IpadOtaRelease[];
      rollout: IpadOtaRollout;
      stores: StoreOption[];
    }
  | {
      kind: "handheld";
      policies: HandheldPolicy[];
      candidates: HandheldCandidate[];
      revisions: Revision[];
    };

const tabs: { value: AppDownloadsSection; label: keyof Copy["tabs"] }[] = [
  { value: "mobile-native", label: "mobileNative" },
  { value: "mobile-ota", label: "mobileOta" },
  { value: "ipad-native", label: "ipadNative" },
  { value: "ipad-ota", label: "ipadOta" },
  { value: "pos-handheld", label: "posHandheld" },
];

// 这些是各原生应用在 EAS 配置中使用的公开 App Store Connect ID；登记时预填，仍允许管理员核对后修改。
const defaultAppStoreIds: Record<AppUpdateApp, string> = {
  "mobile-ios": "6786073002",
  "pos-ipad": "6802176079",
  "pos-handheld": "6802182045",
};

function date(value: string | null | undefined) {
  if (!value) return "—";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString();
}

function resolveWebAppDownloadsUrl() {
  const baseUrl = apiClient.defaults.baseURL;
  try {
    return new URL("/system/app-downloads", baseUrl).toString();
  } catch {
    return null;
  }
}

function useLocalCopy() {
  const { language } = useAppTranslation("common");
  return (language === "en" ? en : zh) as Copy;
}

function validationMessage(copy: Copy, error: PolicyValidationError | null) {
  return error ? copy.validation[error] : "";
}

function registrationMessage(
  copy: Copy,
  error: RegistrationValidationError | null,
) {
  return error ? copy.validation[error] : "";
}

function confirmationLabels(copy: Copy): ConfirmationLabels {
  return {
    enabled: copy.enabledValue,
    disabled: copy.disabledValue,
    release: copy.native.release,
    noRelease: copy.noReleaseValue,
    required: copy.requiredValue,
    optional: copy.optionalValue,
    allDevices: copy.native.all,
    minimumVersion: copy.native.minimumVersion,
    minimumBuild: copy.native.minimumBuild,
    notes: copy.native.message,
    scope: copy.scopeValue,
  };
}

function LinkActions({
  url,
  copy,
}: {
  url: string | null | undefined;
  copy: Copy;
}) {
  const [qr, setQr] = useState(false);
  const [message, setMessage] = useState("");
  const safeUrl = url && /^https?:\/\//i.test(url.trim()) ? url.trim() : null;
  if (!safeUrl) return <Text style={styles.muted}>{copy.noUrl}</Text>;
  return (
    <>
      <View style={styles.inlineActions}>
        <Button
          compact
          icon="open-in-new"
          onPress={() =>
            void Linking.openURL(safeUrl).catch(() => setMessage(copy.error))
          }
        >
          {copy.open}
        </Button>
        <Button
          compact
          icon="content-copy"
          onPress={() =>
            void Clipboard.setStringAsync(safeUrl)
              .then(() => setMessage(copy.copy))
              .catch(() => setMessage(copy.error))
          }
        >
          {copy.copy}
        </Button>
        <Button compact icon="qrcode" onPress={() => setQr(true)}>
          {copy.qr}
        </Button>
        <Button
          compact
          icon="share-variant"
          onPress={() =>
            void Share.share({ message: safeUrl }).catch(() =>
              setMessage(copy.error),
            )
          }
        >
          {copy.share}
        </Button>
      </View>
      <Portal>
        <Modal
          visible={qr}
          onDismiss={() => setQr(false)}
          contentContainerStyle={styles.qrModal}
        >
          <QRCode value={safeUrl} size={220} />
          <Text style={styles.qrUrl}>{safeUrl}</Text>
          <Button onPress={() => setQr(false)}>{copy.close}</Button>
        </Modal>
        <Snackbar visible={Boolean(message)} onDismiss={() => setMessage("")}>
          {message}
        </Snackbar>
      </Portal>
    </>
  );
}

function BuildPanel({
  appKey,
  profile,
  copy,
}: {
  appKey: "mobile" | "pos-handheld";
  profile: AppDownloadEnvironment;
  copy: Copy;
}) {
  const [showHistory, setShowHistory] = useState(false);
  const [page, setPage] = useState(1);
  const latest = useQuery({
    queryKey: ["app-downloads-build-latest", appKey, profile],
    queryFn: () => appDownloadsApi.getLatestBuild(appKey, profile),
  });
  const history = useQuery({
    queryKey: ["app-downloads-build-history", appKey, profile, page],
    queryFn: () => appDownloadsApi.getBuilds(appKey, profile, page),
    enabled: showHistory,
  });
  return (
    <Card style={styles.card}>
      <Card.Title
        title={copy.builds}
        subtitle={`${copy.profile}: ${profile === "production" ? copy.production : copy.preview}`}
      />
      <Card.Content>
        <SegmentedButtons
          value={showHistory ? "history" : "latest"}
          onValueChange={(v) => {
            setShowHistory(v === "history");
            if (v !== "history") setPage(1);
          }}
          buttons={[
            { value: "latest", label: copy.latest },
            { value: "history", label: copy.history },
          ]}
        />
        {latest.isLoading ? (
          <ActivityIndicator />
        ) : latest.error ? (
          <Text style={styles.error}>
            {copy.loadFailed}: {String(latest.error)}
          </Text>
        ) : showHistory ? (
          <>
            {history.isLoading ? (
              <ActivityIndicator />
            ) : history.error ? (
              <Text style={styles.error}>
                {copy.loadFailed}: {String(history.error)}
              </Text>
            ) : (
              <>
                {history.data?.items.map((item) => (
                  <BuildRow key={item.id} build={item} copy={copy} />
                ))}
                <View style={styles.inlineActions}>
                  <Button
                    disabled={page <= 1}
                    onPress={() => setPage((p) => Math.max(1, p - 1))}
                  >
                    ‹
                  </Button>
                  <Text>
                    {history.data?.page ?? page} /{" "}
                    {Math.max(
                      1,
                      Math.ceil(
                        (history.data?.total ?? 0) /
                          (history.data?.pageSize ?? 20),
                      ),
                    )}
                  </Text>
                  <Button
                    disabled={
                      !history.data ||
                      page * history.data.pageSize >= history.data.total
                    }
                    onPress={() => setPage((p) => p + 1)}
                  >
                    ›
                  </Button>
                </View>
              </>
            )}
          </>
        ) : latest.data ? (
          <BuildRow build={latest.data} copy={copy} />
        ) : (
          <Text style={styles.muted}>{copy.empty}</Text>
        )}
      </Card.Content>
    </Card>
  );
}

function BuildRow({ build, copy }: { build: AppBuild; copy: Copy }) {
  const url = build.artifactUrl;
  const source =
    build.cosArtifactUrl && build.artifactUrl === build.cosArtifactUrl
      ? copy.sourceCos
      : copy.sourceEas;
  return (
    <View style={styles.row}>
      <Text variant="titleSmall">
        {build.appVersion ?? "—"} ({build.appBuildVersion ?? "—"})
      </Text>
      <Text>
        {build.platform ?? "—"} · {build.status ?? "—"} ·{" "}
        {date(build.completedAt ?? build.createdAt)}
      </Text>
      <Text>
        {copy.source}: {source} · {copy.mirrorStatus}:{" "}
        {build.cosMirrorStatus ?? copy.noReleaseValue}
      </Text>
      {build.cosMirrorError ? (
        <Text style={styles.error}>{build.cosMirrorError}</Text>
      ) : null}
      <LinkActions url={url} copy={copy} />
      {build.buildDetailsPageUrl ? (
        <LinkActions
          url={build.buildDetailsPageUrl}
          copy={{ ...copy, open: copy.buildDetails }}
        />
      ) : null}
    </View>
  );
}

function NativeSection({
  data,
  copy,
  onSave,
  onRegister,
  saving,
}: {
  data: Extract<SectionData, { kind: "native" }>;
  copy: Copy;
  onSave: (form: NativePolicyForm) => void;
  onRegister: (value: {
    appStoreId: string;
    buildNumber: string;
    storefront: string;
  }) => void;
  saving: boolean;
}) {
  const [form, setForm] = useState<NativePolicyForm>({
    enabled: data.policy.enabled,
    releaseId: data.policy.releaseId ?? "",
    minimumSupportedVersion: data.policy.minimumSupportedVersion ?? "",
    minimumSupportedBuildNumber:
      data.policy.minimumSupportedBuildNumber == null
        ? ""
        : String(data.policy.minimumSupportedBuildNumber),
    releaseMessage: data.policy.releaseMessage ?? "",
    targetScope: data.policy.targetScope,
    targetStoreGuids: data.policy.targetStoreGuids,
  });
  const [registration, setRegistration] = useState({
    appStoreId: defaultAppStoreIds[data.app],
    buildNumber: "",
    storefront: "au",
  });
  const [error, setError] = useState("");
  useEffect(
    () =>
      setForm({
        enabled: data.policy.enabled,
        releaseId: data.policy.releaseId ?? "",
        minimumSupportedVersion: data.policy.minimumSupportedVersion ?? "",
        minimumSupportedBuildNumber:
          data.policy.minimumSupportedBuildNumber == null
            ? ""
            : String(data.policy.minimumSupportedBuildNumber),
        releaseMessage: data.policy.releaseMessage ?? "",
        targetScope: data.policy.targetScope,
        targetStoreGuids: data.policy.targetStoreGuids,
      }),
    [data.policy],
  );
  const set = <K extends keyof NativePolicyForm>(
    key: K,
    value: NativePolicyForm[K],
  ) => setForm((old) => ({ ...old, [key]: value }));
  return (
    <>
      <Card style={styles.card}>
        <Card.Title title={copy.native.register} />
        <Card.Content>
          <TextInput
            label={copy.native.appStoreId}
            value={registration.appStoreId}
            onChangeText={(v) =>
              setRegistration((x) => ({ ...x, appStoreId: v }))
            }
          />
          <TextInput
            label={copy.native.buildNumber}
            value={registration.buildNumber}
            onChangeText={(v) =>
              setRegistration((x) => ({ ...x, buildNumber: v }))
            }
            keyboardType="number-pad"
          />
          <TextInput
            label={copy.native.storefront}
            value={registration.storefront}
            onChangeText={(v) =>
              setRegistration((x) => ({ ...x, storefront: v }))
            }
          />
          <Button
            mode="outlined"
            loading={saving}
            onPress={() => onRegister(registration)}
          >
            {copy.native.registerAction}
          </Button>
          <Text style={styles.muted}>{copy.native.registeredOnly}</Text>
        </Card.Content>
      </Card>
      <Card style={styles.card}>
        <Card.Title
          title={`${copy.native.policy} · ${data.policy.policyVersion}`}
        />
        <Card.Content>
          <SwitchRow
            label={copy.native.enabled}
            value={form.enabled}
            onChange={(v) => set("enabled", v)}
          />
          <TextInput
            label={copy.native.release}
            value={form.releaseId}
            editable={false}
          />
          {data.releases.map((release) => (
            <View key={release.id} style={styles.releaseOption}>
              <Chip
                selected={form.releaseId === release.id}
                onPress={() => set("releaseId", release.id)}
                style={styles.chip}
              >
                {release.version} ({release.buildNumber}) ·{" "}
                {date(release.appleVerifiedAtUtc)}
              </Chip>
              <LinkActions url={release.appStoreUrl} copy={copy} />
            </View>
          ))}
          <TextInput
            label={copy.native.minimumVersion}
            value={form.minimumSupportedVersion}
            onChangeText={(v) => set("minimumSupportedVersion", v)}
          />
          <TextInput
            label={copy.native.minimumBuild}
            value={form.minimumSupportedBuildNumber}
            onChangeText={(v) => set("minimumSupportedBuildNumber", v)}
            keyboardType="number-pad"
          />
          <TextInput
            label={copy.native.message}
            value={form.releaseMessage}
            onChangeText={(v) => set("releaseMessage", v)}
            multiline
          />
          {data.app === "pos-ipad" ? (
            <>
              <Text variant="labelLarge">{copy.native.target}</Text>
              <SegmentedButtons
                value={form.targetScope}
                onValueChange={(v) =>
                  set("targetScope", v as NativePolicyForm["targetScope"])
                }
                buttons={[
                  { value: "all", label: copy.native.all },
                  { value: "stores", label: copy.native.stores },
                ]}
              />
              {form.targetScope === "stores" ? (
                <>
                  {data.stores.map((store) => (
                    <Chip
                      key={store.storeGuid}
                      selected={form.targetStoreGuids.includes(store.storeGuid)}
                      onPress={() =>
                        set(
                          "targetStoreGuids",
                          form.targetStoreGuids.includes(store.storeGuid)
                            ? form.targetStoreGuids.filter(
                                (guid) => guid !== store.storeGuid,
                              )
                            : [...form.targetStoreGuids, store.storeGuid],
                        )
                      }
                      style={styles.chip}
                    >
                      {store.storeCode} · {store.storeName}
                    </Chip>
                  ))}
                  <TextInput
                    label={copy.native.storesPlaceholder}
                    value={form.targetStoreGuids.join(", ")}
                    editable={false}
                  />
                </>
              ) : null}
            </>
          ) : null}
          {error ? <Text style={styles.error}>{error}</Text> : null}
          <Button
            mode="contained"
            loading={saving}
            onPress={() => {
              const validation = validateNativePolicy(
                form,
                data.app === "pos-ipad",
              );
              if (validation) setError(validationMessage(copy, validation));
              else {
                setError("");
                onSave(form);
              }
            }}
          >
            {copy.native.save}
          </Button>
        </Card.Content>
      </Card>
    </>
  );
}

function SwitchRow({
  label,
  value,
  onChange,
}: {
  label: string;
  value: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <View style={styles.switchRow}>
      <Text>{label}</Text>
      <Switch value={value} onValueChange={onChange} />
    </View>
  );
}

function OtaSection({
  data,
  copy,
  onSave,
  saving,
}: {
  data: Extract<SectionData, { kind: "mobile-ota" }>;
  copy: Copy;
  onSave: (form: OtaPolicyForm) => void;
  saving: boolean;
}) {
  const [form, setForm] = useState<OtaPolicyForm>({
    enabled: data.policy.enabled,
    required: data.policy.required,
    targetReleaseId: data.policy.targetReleaseId ?? "",
    releaseMessage: data.policy.releaseMessage ?? "",
  });
  const [error, setError] = useState("");
  useEffect(
    () =>
      setForm({
        enabled: data.policy.enabled,
        required: data.policy.required,
        targetReleaseId: data.policy.targetReleaseId ?? "",
        releaseMessage: data.policy.releaseMessage ?? "",
      }),
    [data.policy],
  );
  return (
    <>
      <Card style={styles.card}>
        <Card.Title
          title={`${copy.ota.releases} · ${data.policy.environment}/${data.policy.platform}`}
        />
        <Card.Content>
          <SwitchRow
            label={copy.ota.enabled}
            value={form.enabled}
            onChange={(v) => setForm((x) => ({ ...x, enabled: v }))}
          />
          <SwitchRow
            label={copy.ota.required}
            value={form.required}
            onChange={(v) => setForm((x) => ({ ...x, required: v }))}
          />
          <TextInput
            label={copy.ota.target}
            value={form.targetReleaseId}
            editable={false}
          />
          {data.releases.map((release) => (
            <Chip
              key={release.id}
              selected={form.targetReleaseId === release.id}
              onPress={() =>
                setForm((x) => ({ ...x, targetReleaseId: release.id }))
              }
              style={styles.chip}
            >
              {release.runtimeVersion} · {release.updateGroupId} ·{" "}
              {date(release.publishedAtUtc)}
            </Chip>
          ))}
          <TextInput
            label={copy.ota.message}
            value={form.releaseMessage}
            onChangeText={(v) => setForm((x) => ({ ...x, releaseMessage: v }))}
            multiline
          />
          {error ? <Text style={styles.error}>{error}</Text> : null}
          <Button
            mode="contained"
            loading={saving}
            onPress={() => {
              const validation = validateOtaPolicy(form);
              if (validation) setError(validationMessage(copy, validation));
              else {
                setError("");
                onSave(form);
              }
            }}
          >
            {copy.ota.save}
          </Button>
        </Card.Content>
      </Card>
      <RevisionCard revisions={data.revisions} title={copy.ota.revisions} />
    </>
  );
}

function IpadOtaSection({
  data,
  copy,
  onSave,
  saving,
}: {
  data: Extract<SectionData, { kind: "ipad-ota" }>;
  copy: Copy;
  onSave: (form: OtaPolicyForm) => void;
  saving: boolean;
}) {
  const rollout = data.rollout;
  const [form, setForm] = useState<OtaPolicyForm>({
    enabled: rollout.enabled,
    required: rollout.forceUpdate,
    targetReleaseId: rollout.releaseId ?? "",
    releaseMessage: rollout.releaseMessage ?? "",
    targetScope: rollout.targetScope,
    targetStoreGuids: rollout.targetStoreGuids,
  });
  const [error, setError] = useState("");
  useEffect(
    () =>
      setForm({
        enabled: rollout.enabled,
        required: rollout.forceUpdate,
        targetReleaseId: rollout.releaseId ?? "",
        releaseMessage: rollout.releaseMessage ?? "",
        targetScope: rollout.targetScope,
        targetStoreGuids: rollout.targetStoreGuids,
      }),
    [rollout],
  );
  const set = <K extends keyof OtaPolicyForm>(
    key: K,
    value: OtaPolicyForm[K],
  ) => setForm((old) => ({ ...old, [key]: value }));
  return (
    <Card style={styles.card}>
      <Card.Title title={copy.ota.releases} />
      <Card.Content>
        <SwitchRow
          label={copy.ota.enabled}
          value={form.enabled}
          onChange={(enabled) => set("enabled", enabled)}
        />
        <SwitchRow
          label={copy.ota.required}
          value={form.required}
          onChange={(required) => set("required", required)}
        />
        <TextInput
          label={copy.ota.target}
          value={form.targetReleaseId}
          editable={false}
        />
        {data.releases.map((release) => (
          <Chip
            key={release.id}
            selected={form.targetReleaseId === release.id}
            onPress={() => set("targetReleaseId", release.id)}
            style={styles.chip}
          >
            {release.runtimeVersion} · {release.updateGroupId} ·{" "}
            {date(release.publishedAtUtc)}
          </Chip>
        ))}
        <TextInput
          label={copy.ota.message}
          value={form.releaseMessage}
          onChangeText={(message) => set("releaseMessage", message)}
          multiline
        />
        <Text variant="labelLarge">{copy.native.target}</Text>
        <SegmentedButtons
          value={form.targetScope ?? "all"}
          onValueChange={(scope) =>
            set("targetScope", scope as OtaPolicyForm["targetScope"])
          }
          buttons={[
            { value: "all", label: copy.native.all },
            { value: "stores", label: copy.native.stores },
          ]}
        />
        {form.targetScope === "stores" ? (
          <>
            {data.stores.map((store) => (
              <Chip
                key={store.storeGuid}
                selected={form.targetStoreGuids?.includes(store.storeGuid)}
                onPress={() =>
                  set(
                    "targetStoreGuids",
                    form.targetStoreGuids?.includes(store.storeGuid)
                      ? (form.targetStoreGuids ?? []).filter(
                          (guid) => guid !== store.storeGuid,
                        )
                      : [...(form.targetStoreGuids ?? []), store.storeGuid],
                  )
                }
                style={styles.chip}
              >
                {store.storeCode} · {store.storeName}
              </Chip>
            ))}
            <TextInput
              label={copy.native.storesPlaceholder}
              value={(form.targetStoreGuids ?? []).join(", ")}
              editable={false}
            />
          </>
        ) : null}
        {error ? <Text style={styles.error}>{error}</Text> : null}
        <Button
          mode="contained"
          loading={saving}
          disabled={saving}
          onPress={() => {
            const validation =
              validateOtaPolicy(form) ??
              validateTargetScope(form.targetScope, form.targetStoreGuids);
            if (validation) setError(validationMessage(copy, validation));
            else {
              setError("");
              onSave(form);
            }
          }}
        >
          {copy.ota.save}
        </Button>
      </Card.Content>
    </Card>
  );
}

function HandheldSection({
  data,
  copy,
  onSave,
  onRegister,
  saving,
}: {
  data: Extract<SectionData, { kind: "handheld" }>;
  copy: Copy;
  onSave: (policy: HandheldPolicy, form: NativePolicyForm) => void;
  onRegister: (value: {
    appStoreId: string;
    buildNumber: string;
    storefront: string;
  }) => void;
  saving: boolean;
}) {
  const [lane, setLane] = useState(data.policies[0]?.lane ?? "android-native");
  const policy = data.policies.find((item) => item.lane === lane);
  const candidates = data.candidates.filter((item) => item.lane === lane);
  const [form, setForm] = useState<NativePolicyForm>({
    enabled: policy?.enabled ?? false,
    required: policy?.required ?? false,
    releaseId: policy?.candidateId ?? "",
    minimumSupportedVersion: policy?.minimumSupportedVersion ?? "",
    minimumSupportedBuildNumber:
      policy?.minimumSupportedBuildNumber == null
        ? ""
        : String(policy.minimumSupportedBuildNumber),
    releaseMessage: policy?.releaseMessage ?? "",
    targetScope: "all",
    targetStoreGuids: [],
  });
  const [registration, setRegistration] = useState({
    appStoreId: defaultAppStoreIds["pos-handheld"],
    buildNumber: "",
    storefront: "au",
  });
  useEffect(() => {
    if (policy)
      setForm({
        enabled: policy.enabled,
        required: policy.required,
        releaseId: policy.candidateId ?? "",
        minimumSupportedVersion: policy.minimumSupportedVersion ?? "",
        minimumSupportedBuildNumber:
          policy.minimumSupportedBuildNumber == null
            ? ""
            : String(policy.minimumSupportedBuildNumber),
        releaseMessage: policy.releaseMessage ?? "",
        targetScope: "all",
        targetStoreGuids: [],
      });
  }, [policy]);
  if (!policy) return <Text style={styles.muted}>{copy.empty}</Text>;
  return (
    <>
      <Card style={styles.card}>
        <Card.Title title={copy.native.register} />
        <Card.Content>
          <TextInput
            label={copy.native.appStoreId}
            value={registration.appStoreId}
            onChangeText={(v) =>
              setRegistration((x) => ({ ...x, appStoreId: v }))
            }
          />
          <TextInput
            label={copy.native.buildNumber}
            value={registration.buildNumber}
            onChangeText={(v) =>
              setRegistration((x) => ({ ...x, buildNumber: v }))
            }
          />
          <TextInput
            label={copy.native.storefront}
            value={registration.storefront}
            onChangeText={(v) =>
              setRegistration((x) => ({ ...x, storefront: v }))
            }
          />
          <Button
            mode="outlined"
            loading={saving}
            onPress={() => onRegister(registration)}
          >
            {copy.native.registerAction}
          </Button>
          <Text style={styles.muted}>{copy.native.registeredOnly}</Text>
        </Card.Content>
      </Card>
      <Card style={styles.card}>
        <Card.Title
          title={`${copy.handheld.lane} · ${lane} · ${policy.policyVersion}`}
        />
        <Card.Content>
          <SegmentedButtons
            value={lane}
            onValueChange={setLane}
            buttons={[
              {
                value: "android-native",
                label: "Android native",
                disabled: saving,
              },
              { value: "ios-native", label: "iOS native", disabled: saving },
              { value: "android-ota", label: "Android OTA", disabled: saving },
              { value: "ios-ota", label: "iOS OTA", disabled: saving },
            ]}
          />
          <SwitchRow
            label={copy.handheld.enabled}
            value={form.enabled}
            onChange={(enabled) => setForm((x) => ({ ...x, enabled }))}
          />
          <SwitchRow
            label={copy.handheld.required}
            value={form.required ?? false}
            onChange={(required) => setForm((x) => ({ ...x, required }))}
          />
          <TextInput
            label={copy.handheld.candidate}
            value={form.releaseId}
            editable={false}
          />
          {candidates.map((item) => (
            <Chip
              key={item.id}
              selected={form.releaseId === item.id}
              disabled={!item.activatable || saving}
              onPress={() => setForm((x) => ({ ...x, releaseId: item.id }))}
              style={styles.chip}
            >
              {item.version ?? "—"} ·{" "}
              {item.buildNumber ?? item.runtimeVersion ?? "—"} ·{" "}
              {item.activatable
                ? "activatable"
                : (item.blockedReason ?? "blocked")}
            </Chip>
          ))}
          {lane.endsWith("native") ? (
            <TextInput
              label={copy.native.minimumVersion}
              value={form.minimumSupportedVersion}
              onChangeText={(v) =>
                setForm((x) => ({ ...x, minimumSupportedVersion: v }))
              }
            />
          ) : null}
          {lane.endsWith("native") ? (
            <TextInput
              label={copy.native.minimumBuild}
              value={form.minimumSupportedBuildNumber}
              onChangeText={(v) =>
                setForm((x) => ({ ...x, minimumSupportedBuildNumber: v }))
              }
              keyboardType="number-pad"
            />
          ) : null}
          <TextInput
            label={copy.native.message}
            value={form.releaseMessage}
            onChangeText={(v) => setForm((x) => ({ ...x, releaseMessage: v }))}
            multiline
          />
          <Button
            mode="contained"
            loading={saving}
            onPress={() => onSave(policy, form)}
          >
            {copy.handheld.save}
          </Button>
        </Card.Content>
      </Card>
      <RevisionCard
        revisions={data.revisions.filter((item) => item.lane === policy.lane)}
        title={copy.handheld.revisions}
      />
    </>
  );
}

function RevisionCard({
  revisions,
  title,
}: {
  revisions: Revision[];
  title: string;
}) {
  return (
    <Card style={styles.card}>
      <Card.Title title={title} />
      <Card.Content>
        {revisions.map((item) => (
          <View key={item.id} style={styles.row}>
            <Text>
              {item.operation || "—"} · v{item.policyVersion}
            </Text>
            <Text>
              {date(item.createdAt)} · {item.createdBy ?? "—"}
            </Text>
          </View>
        ))}
      </Card.Content>
    </Card>
  );
}

export default function AppDownloadsScreen() {
  const copy = useLocalCopy();
  const [section, setSection] = useState<AppDownloadsSection>("mobile-native");
  const [profile, setProfile] = useState<AppDownloadEnvironment>("production");
  const [platform, setPlatform] = useState<AppDownloadPlatform>("ios");
  const [pending, setPending] = useState<{
    summary: string[];
    execute: () => Promise<void>;
  } | null>(null);
  const [saving, setSaving] = useState(false);
  const [snack, setSnack] = useState("");
  const saveInFlightRef = useRef(false);
  const queryKey = useMemo(
    () => ["app-downloads-section", section, profile, platform] as const,
    [section, profile, platform],
  );
  const query = useQuery<SectionData>({
    queryKey,
    queryFn: async () => {
      if (section === "mobile-native" || section === "ipad-native") {
        const app = section === "mobile-native" ? "mobile-ios" : "pos-ipad";
        const [releases, policy, stores] = await Promise.all([
          appDownloadsApi.getIosReleases(app),
          appDownloadsApi.getNativePolicy(app),
          section === "ipad-native"
            ? appDownloadsApi.getStoreOptions()
            : Promise.resolve([]),
        ]);
        return { kind: "native", app, releases, policy, stores };
      }
      if (section === "mobile-ota") {
        const [releases, policy, revisions] = await Promise.all([
          appDownloadsApi.getMobileOtaReleases(profile, platform),
          appDownloadsApi.getMobileOtaPolicy(profile, platform),
          appDownloadsApi.getMobileOtaRevisions(profile, platform),
        ]);
        return { kind: "mobile-ota", releases, policy, revisions };
      }
      if (section === "ipad-ota") {
        const [releases, rollout, stores] = await Promise.all([
          appDownloadsApi.getIpadOtaReleases(),
          appDownloadsApi.getIpadOtaRollout(),
          appDownloadsApi.getStoreOptions(),
        ]);
        return { kind: "ipad-ota", releases, rollout, stores };
      }
      const [policies, candidates, revisions] = await Promise.all([
        appDownloadsApi.getHandheldPolicies(),
        Promise.all(
          (
            ["android-native", "ios-native", "android-ota", "ios-ota"] as const
          ).map((targetLane) =>
            appDownloadsApi.getHandheldCandidates(
              targetLane.startsWith("ios") ? "ios" : "android",
              targetLane.endsWith("ota") ? "ota" : "native",
            ),
          ),
        ).then((values) => values.flat()),
        Promise.all(
          (
            ["android-native", "ios-native", "android-ota", "ios-ota"] as const
          ).map((targetLane) =>
            appDownloadsApi.getHandheldRevisions(targetLane),
          ),
        ).then((values) => values.flat()),
      ]);
      return {
        kind: "handheld",
        policies,
        candidates: mergeHandheldPolicyCandidates(candidates, policies),
        revisions,
      };
    },
    retry: 1,
  });
  const buildAppKey = section === "pos-handheld" ? "pos-handheld" : "mobile";
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
  const register = async (
    app: AppUpdateApp,
    value: { appStoreId: string; buildNumber: string; storefront: string },
  ) => {
    if (saveInFlightRef.current || saving || pending) return;
    const registrationError = validateRegistration(app, value);
    if (registrationError) {
      setSnack(registrationMessage(copy, registrationError));
      return;
    }
    setSaving(true);
    saveInFlightRef.current = true;
    try {
      await appDownloadsApi.registerIosRelease({
        app,
        ...value,
        appStoreId: value.appStoreId.trim(),
        buildNumber: value.buildNumber.trim(),
        storefront: value.storefront.trim().toLowerCase(),
      });
      await readBack();
      setSnack(copy.registered);
    } catch (error) {
      setSnack(
        error instanceof Error && error.message === copy.readbackFailed
          ? copy.readbackFailed
          : `${copy.error}: ${String(error)}`,
      );
    } finally {
      setSaving(false);
      saveInFlightRef.current = false;
    }
  };
  const confirmSave = (summary: string[], execute: () => Promise<void>) =>
    !saveInFlightRef.current &&
    !saving &&
    !pending &&
    setPending({ summary, execute });
  const saveNative = (
    form: NativePolicyForm,
    data: Extract<SectionData, { kind: "native" }>,
  ) =>
    confirmSave(
      buildConfirmationSummary(
        form,
        {
          releaseLabel:
            data.releases.find((x) => x.id === form.releaseId)?.version ??
            form.releaseId,
          targetLabel:
            form.targetScope === "stores"
              ? `${form.targetStoreGuids.length} stores`
              : copy.native.all,
        },
        confirmationLabels(copy),
      ),
      async () => {
        const payload = buildNativePolicyPayload(
          form,
          data.policy.policyVersion,
          data.app === "pos-ipad",
        );
        await appDownloadsApi.saveNativePolicy(data.app, payload);
        await readBack();
      },
    );
  const saveOta = (
    form: OtaPolicyForm,
    data: Extract<SectionData, { kind: "mobile-ota" }>,
  ) =>
    confirmSave(
      buildConfirmationSummary(
        form,
        {
          releaseLabel:
            data.releases.find((x) => x.id === form.targetReleaseId)
              ?.updateGroupId ?? form.targetReleaseId,
        },
        confirmationLabels(copy),
      ),
      async () => {
        await appDownloadsApi.saveMobileOtaPolicy(
          data.policy.environment,
          data.policy.platform,
          buildOtaPolicyPayload(form, data.policy.policyVersion),
        );
        await readBack();
      },
    );
  const saveIpadOta = (
    form: OtaPolicyForm,
    data: Extract<SectionData, { kind: "ipad-ota" }>,
  ) =>
    confirmSave(
      buildConfirmationSummary(
        form,
        {
          releaseLabel:
            data.releases.find((x) => x.id === form.targetReleaseId)
              ?.updateGroupId ?? form.targetReleaseId,
          targetLabel:
            form.targetScope === "stores"
              ? `${form.targetStoreGuids?.length ?? 0} stores`
              : copy.native.all,
        },
        confirmationLabels(copy),
      ),
      async () => {
        await appDownloadsApi.saveIpadOtaRollout({
          expectedPolicyVersion: data.rollout.policyVersion,
          enabled: form.enabled,
          releaseId: form.enabled ? form.targetReleaseId || null : null,
          forceUpdate: form.enabled && form.required,
          targetScope:
            form.enabled && form.targetScope === "stores" ? "stores" : "all",
          targetStoreGuids:
            form.enabled && form.targetScope === "stores"
              ? (form.targetStoreGuids ?? [])
              : [],
          releaseMessage: form.enabled
            ? form.releaseMessage.trim() || null
            : null,
        });
        await readBack();
      },
    );
  const saveHandheld = (policy: HandheldPolicy, form: NativePolicyForm) => {
    const selectedCandidate =
      query.data?.kind === "handheld"
        ? (query.data.candidates.find(
            (item) => item.id === form.releaseId && item.lane === policy.lane,
          ) ?? null)
        : null;
    const candidateValid =
      form.releaseId !== policy.candidateId || policy.candidateValid;
    const validation = validateHandheldPolicy(
      form,
      policy.lane,
      selectedCandidate,
      candidateValid,
      policy.blockedReason,
    );
    if (validation) {
      setSnack(validationMessage(copy, validation));
      return;
    }
    confirmSave(
      buildConfirmationSummary(
        form,
        { releaseLabel: form.releaseId },
        confirmationLabels(copy),
      ),
      async () => {
        await appDownloadsApi.saveHandheldPolicy(policy.lane, {
          expectedPolicyVersion: policy.policyVersion,
          enabled: form.enabled,
          required: form.enabled && form.required === true,
          candidateId: form.enabled ? form.releaseId.trim() || null : null,
          minimumSupportedVersion:
            form.enabled && policy.lane.endsWith("native")
              ? form.minimumSupportedVersion.trim() || null
              : null,
          minimumSupportedBuildNumber:
            form.enabled &&
            policy.lane.endsWith("native") &&
            form.minimumSupportedBuildNumber.trim()
              ? Number(form.minimumSupportedBuildNumber)
              : null,
          releaseMessage: form.enabled
            ? form.releaseMessage.trim() || null
            : null,
        });
        await readBack();
      },
    );
  };
  const executePending = async () => {
    if (!pending || saveInFlightRef.current) return;
    setSaving(true);
    saveInFlightRef.current = true;
    try {
      await pending.execute();
      setSnack(copy.saved);
    } catch (error) {
      if (isPolicyConflict(error)) {
        try {
          await readBack();
          setSnack(copy.conflict);
        } catch {
          setSnack(`${copy.conflict} · ${copy.readbackFailed}`);
        }
      } else if (
        error instanceof Error &&
        error.message === copy.readbackFailed
      )
        setSnack(copy.readbackFailed);
      else setSnack(`${copy.error}: ${String(error)}`);
    } finally {
      setSaving(false);
      saveInFlightRef.current = false;
      setPending(null);
    }
  };
  const loadedData = query.data;
  const locked = saving || Boolean(pending) || saveInFlightRef.current;
  const webAppDownloadsUrl = resolveWebAppDownloadsUrl();
  let sectionContent: ReactNode = null;
  if (query.isLoading)
    sectionContent = <ActivityIndicator style={styles.loading} />;
  else if (query.error)
    sectionContent = (
      <Card style={styles.card}>
        <Card.Content>
          <Text style={styles.error}>
            {copy.loadFailed}: {String(query.error)}
          </Text>
          <Button onPress={() => void query.refetch()}>{copy.retry}</Button>
        </Card.Content>
      </Card>
    );
  else if (loadedData?.kind === "native")
    sectionContent = (
      <NativeSection
        data={loadedData}
        copy={copy}
        saving={locked}
        onRegister={(value) => void register(loadedData.app, value)}
        onSave={(form) => saveNative(form, loadedData)}
      />
    );
  else if (loadedData?.kind === "mobile-ota")
    sectionContent = (
      <>
        <View style={styles.filterRow}>
          <Text variant="labelLarge">{copy.ota.platform}</Text>
          <SegmentedButtons
            value={platform}
            onValueChange={(v) => setPlatform(v as AppDownloadPlatform)}
            buttons={[
              { value: "android", label: copy.ota.android, disabled: locked },
              { value: "ios", label: copy.ota.ios, disabled: locked },
            ]}
          />
        </View>
        <OtaSection
          data={loadedData}
          copy={copy}
          saving={locked}
          onSave={(form) => saveOta(form, loadedData)}
        />
      </>
    );
  else if (loadedData?.kind === "ipad-ota")
    sectionContent = (
      <IpadOtaSection
        data={loadedData}
        copy={copy}
        saving={locked}
        onSave={(form) => saveIpadOta(form, loadedData)}
      />
    );
  else if (loadedData?.kind === "handheld")
    sectionContent = (
      <HandheldSection
        data={loadedData}
        copy={copy}
        saving={locked}
        onRegister={(value) => void register("pos-handheld", value)}
        onSave={(policy, form) => saveHandheld(policy, form)}
      />
    );
  return (
    <View style={styles.container}>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={styles.titleRow}>
          <Text variant="headlineSmall" style={styles.title}>
            {copy.title}
          </Text>
          {webAppDownloadsUrl ? (
            <Button
              compact
              icon="web"
              onPress={() =>
                void Linking.openURL(webAppDownloadsUrl).catch(() =>
                  setSnack(copy.error),
                )
              }
            >
              {copy.openWeb}
            </Button>
          ) : null}
        </View>
        <ScrollView
          horizontal
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={styles.tabsRow}
        >
          {tabs.map((tab) => (
            <Chip
              key={tab.value}
              selected={section === tab.value}
              disabled={locked}
              onPress={() => setSection(tab.value)}
              style={styles.tabChip}
            >
              {copy.tabs[tab.label]}
            </Chip>
          ))}
        </ScrollView>
        <View style={styles.filterRow}>
          <Text variant="labelLarge">{copy.profile}</Text>
          <SegmentedButtons
            value={profile}
            onValueChange={(v) => setProfile(v as AppDownloadEnvironment)}
            buttons={[
              { value: "production", label: copy.production, disabled: locked },
              { value: "preview", label: copy.preview, disabled: locked },
            ]}
          />
        </View>
        {section !== "ipad-native" && section !== "ipad-ota" ? (
          <BuildPanel appKey={buildAppKey} profile={profile} copy={copy} />
        ) : null}
        {sectionContent}
      </ScrollView>
      <Portal>
        <Modal
          visible={Boolean(pending)}
          style={{ justifyContent: "flex-end" }}
          onDismiss={() => {
            if (!saving) setPending(null);
          }}
          contentContainerStyle={styles.confirmModal}
        >
          <Text variant="titleLarge">{copy.confirm.title}</Text>
          {pending?.summary.map((line) => (
            <Text key={line} style={styles.summaryLine}>
              {line}
            </Text>
          ))}
          <View style={styles.inlineActions}>
            <Button onPress={() => setPending(null)} disabled={saving}>
              {copy.confirm.cancel}
            </Button>
            <Button
              mode="contained"
              onPress={() => void executePending()}
              loading={saving}
            >
              {copy.confirm.confirm}
            </Button>
          </View>
        </Modal>
        <Snackbar visible={Boolean(snack)} onDismiss={() => setSnack("")}>
          {snack}
        </Snackbar>
      </Portal>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { ...BUSINESS_UI.screen },
  content: { padding: 16, gap: 12, paddingBottom: 48 },
  title: { ...BUSINESS_UI.title, marginBottom: 4 },
  titleRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  tabsRow: { gap: 8, paddingRight: 12 },
  tabChip: { minWidth: 112, borderRadius: 8 },
  card: { ...BUSINESS_UI.section, marginVertical: 4 },
  row: { paddingVertical: 12, gap: 6, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: HB_COLORS.outlineMuted },
  muted: { color: HB_COLORS.textSecondary },
  error: { color: "#b42318", marginVertical: 6 },
  loading: { margin: 24 },
  filterRow: { ...BUSINESS_UI.filterGroup },
  inlineActions: {
    flexDirection: "row",
    flexWrap: "wrap",
    alignItems: "center",
    gap: 2,
  },
  switchRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    minHeight: 48,
    paddingVertical: 8,
  },
  chip: { marginVertical: 3 },
  releaseOption: { gap: 2 },
  qrModal: {
    backgroundColor: "white",
    margin: 24,
    padding: 24,
    borderRadius: 12,
    alignItems: "center",
    gap: 14,
  },
  qrUrl: { textAlign: "center" },
  confirmModal: {
    backgroundColor: "white",
    alignSelf: "center",
    width: "100%",
    maxWidth: 680,
    padding: 16,
    paddingBottom: 28,
    borderTopLeftRadius: 18,
    borderTopRightRadius: 18,
    gap: 10,
  },
  summaryLine: { fontSize: 15 },
});
