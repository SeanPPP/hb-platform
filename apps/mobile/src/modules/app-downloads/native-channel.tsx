import { useEffect, useRef, useState, type ReactNode } from "react";
import { StyleSheet, View } from "react-native";
import { HB_SPACING } from "@/shared/theme/tokens";
import { fmt, formatDateTime, type Copy, type SaveResult } from "./copy";
import {
  AndroidBuildCard,
  BuildHistorySheet,
  IosDownloadCard,
  androidQrTarget,
  androidShareUrl,
  storeQrTarget,
  useLatestBuild,
  type StoreDownload,
} from "./download-cards";
import {
  pickHandheldIosDownload,
  pickNativeDownloadRelease,
  type AppDownloadsApp,
} from "./logic";
import {
  HandheldNativeEditor,
  NativePolicyEditor,
  PolicySummaryCard,
  RegisterSheet,
  ReleaseListCard,
  RevisionsSheet,
  candidateLabel,
  releaseLabel,
} from "./policy-sheets";
import type { SectionData } from "./section-data";
import { QrShareSheet, useLinkActions, type QrTarget } from "./share-sheet";
import type {
  AppDownloadEnvironment,
  AppUpdateApp,
  HandheldPolicy,
  NativePolicy,
  NativePolicyForm,
  PolicyLane,
} from "./types";
import { InfoNote, NavRow, ScreenFrame, SectionHeader } from "./ui";

type NativeData = Extract<SectionData, { kind: "native" | "handheld" }>;
type Sheet =
  | { kind: "qr"; key?: string; targets?: QrTarget[] }
  | { kind: "register" }
  | { kind: "editor"; lane?: PolicyLane }
  | { kind: "history" }
  | { kind: "revisions" };

const NATIVE_LANES = ["android-native", "ios-native"] as const;

function nativeSummaryRows(policy: NativePolicy, data: NativeData, copy: Copy) {
  if (data.kind !== "native") return [];
  const release = data.releases.find((item) => item.id === policy.releaseId);
  const rows = [
    {
      label: copy.policy.release,
      value: release
        ? releaseLabel(release)
        : (policy.latestVersion ?? copy.policy.notSet),
      strong: Boolean(release),
      muted: !release,
    },
    {
      label: copy.policy.minimumVersion,
      value: policy.minimumSupportedVersion
        ? policy.minimumSupportedBuildNumber == null
          ? policy.minimumSupportedVersion
          : `${policy.minimumSupportedVersion} (${policy.minimumSupportedBuildNumber})`
        : copy.policy.notSet,
      muted: !policy.minimumSupportedVersion,
    },
    {
      label: copy.policy.message,
      value: policy.releaseMessage ?? copy.policy.notFilled,
      muted: !policy.releaseMessage,
    },
  ];
  if (data.app === "pos-ipad")
    rows.push({
      label: copy.policy.scope,
      value:
        policy.targetScope === "stores"
          ? fmt(copy.policy.stores, { count: policy.targetStoreGuids.length })
          : copy.policy.allDevices,
      muted: false,
    });
  if (policy.updatedAt)
    rows.push({
      label: copy.policy.updated,
      value: `${policy.updatedBy ?? "—"} · ${formatDateTime(policy.updatedAt)}`,
      muted: false,
    });
  return rows;
}

function handheldSummaryRows(
  policy: HandheldPolicy,
  data: NativeData,
  copy: Copy,
) {
  const candidate =
    (data.kind === "handheld"
      ? data.candidates.find(
          (item) => item.id === policy.candidateId && item.lane === policy.lane,
        )
      : undefined) ?? policy.candidate;
  return [
    {
      label: copy.policy.release,
      // 绑定的候选已退出候选列表时，仍显示其 ID 前缀，方便管理员核对
      value: candidate
        ? candidateLabel(candidate)
        : policy.candidateId
          ? policy.candidateId.slice(0, 8)
          : copy.policy.notSet,
      strong: Boolean(candidate),
      muted: !candidate,
    },
    {
      label: copy.policy.minimumVersion,
      value: policy.minimumSupportedVersion
        ? policy.minimumSupportedBuildNumber == null
          ? policy.minimumSupportedVersion
          : `${policy.minimumSupportedVersion} (${policy.minimumSupportedBuildNumber})`
        : copy.policy.notSet,
      muted: !policy.minimumSupportedVersion,
    },
    {
      label: copy.policy.message,
      value: policy.releaseMessage ?? copy.policy.notFilled,
      muted: !policy.releaseMessage,
    },
  ];
}

export function NativeChannel({
  header,
  app,
  data,
  profile,
  onProfileChange,
  apiBaseUrl,
  copy,
  notify,
  saving,
  refreshing,
  onRefresh,
  onSaveNative,
  onSaveHandheld,
  onRegister,
}: {
  header: ReactNode;
  app: AppDownloadsApp;
  data: NativeData;
  profile: AppDownloadEnvironment;
  onProfileChange: (profile: AppDownloadEnvironment) => void;
  apiBaseUrl: string | undefined;
  copy: Copy;
  notify: (message: string) => void;
  saving: boolean;
  refreshing: boolean;
  onRefresh: () => void;
  onSaveNative: (form: NativePolicyForm) => Promise<SaveResult>;
  onSaveHandheld: (
    policy: HandheldPolicy,
    form: NativePolicyForm,
  ) => Promise<SaveResult>;
  onRegister: (
    app: AppUpdateApp,
    value: { appStoreId: string; buildNumber: string; storefront: string },
  ) => Promise<SaveResult>;
}) {
  const [sheet, setSheet] = useState<Sheet | null>(null);
  const switchTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (switchTimer.current) clearTimeout(switchTimer.current);
    },
    [],
  );
  const links = useLinkActions(copy, notify);
  const handheld = data.kind === "handheld";
  const hasAndroid = app !== "ipad";
  const buildAppKey = handheld ? "pos-handheld" : "mobile";
  // 手持只接收生产 APK，环境固定为生产
  const buildProfile: AppDownloadEnvironment = handheld
    ? "production"
    : profile;
  const latest = useLatestBuild(buildAppKey, buildProfile, hasAndroid);
  const environmentLabel = copy.environments[buildProfile];
  const androidUrl = hasAndroid
    ? androidShareUrl(latest.data, buildAppKey, buildProfile, apiBaseUrl)
    : null;
  const androidTarget =
    latest.data && androidUrl
      ? androidQrTarget(latest.data, androidUrl, environmentLabel, copy)
      : null;

  const storeLabel = app === "ipad" ? copy.platforms.ipad : copy.platforms.ios;
  let store: StoreDownload | null = null;
  if (data.kind === "native") {
    const picked = pickNativeDownloadRelease(data.releases, data.policy);
    if (picked)
      store = {
        version: picked.release.version,
        buildNumber: picked.release.buildNumber,
        url: picked.release.appStoreUrl,
        meta: fmt(copy.download.verifiedAt, {
          time: formatDateTime(picked.release.appleVerifiedAtUtc),
        }),
        region: picked.release.storefront,
        active: picked.active,
      };
  } else {
    const picked = pickHandheldIosDownload(data.policies, data.candidates);
    if (picked)
      store = {
        version: picked.candidate.version ?? "—",
        buildNumber: picked.candidate.buildNumber,
        url: picked.candidate.appStoreUrl,
        meta: fmt(copy.download.createdAt, {
          time: formatDateTime(picked.candidate.createdAt),
        }),
        region: null,
        active: picked.active,
      };
  }
  const storeTarget = store ? storeQrTarget(store, storeLabel, copy) : null;
  const qrTargets = [storeTarget, androidTarget].filter(
    (item): item is QrTarget => Boolean(item),
  );
  const appName = copy.appNames[app];

  const registerApp: AppUpdateApp =
    data.kind === "native" ? data.app : "pos-handheld";
  const releaseRows =
    data.kind === "native"
      ? data.releases.map((release) => ({
          id: release.id,
          version: release.version,
          build: release.buildNumber,
          meta: fmt(copy.download.verifiedAt, {
            time: formatDateTime(release.appleVerifiedAtUtc, false),
          }),
          current: release.id === data.policy.releaseId,
        }))
      : data.candidates
          .filter((item) => item.lane === "ios-native")
          .map((item) => ({
            id: item.id,
            version: item.version ?? "—",
            build: item.buildNumber,
            meta: fmt(copy.download.createdAt, {
              time: formatDateTime(item.createdAt, false),
            }),
            current:
              item.id ===
              data.policies.find((policy) => policy.lane === "ios-native")
                ?.candidateId,
          }));
  const latestLabel = releaseRows[0]
    ? releaseRows[0].build
      ? `${releaseRows[0].version} (${releaseRows[0].build})`
      : releaseRows[0].version
    : null;

  const openQr = (key: string) => setSheet({ kind: "qr", key });
  const storeCard = (
    <IosDownloadCard
      key="store"
      label={storeLabel}
      download={store}
      copy={copy}
      links={links}
      onQr={() => openQr("ios")}
      onRegister={() => setSheet({ kind: "register" })}
    />
  );
  const androidCard = hasAndroid ? (
    <AndroidBuildCard
      key="android"
      latest={latest}
      shareUrl={androidUrl}
      profile={buildProfile}
      onProfileChange={handheld ? undefined : onProfileChange}
      copy={copy}
      links={links}
      onQr={() => openQr("android")}
      onHistory={() => setSheet({ kind: "history" })}
      disabled={saving}
    />
  ) : null;

  const editingLane = sheet?.kind === "editor" ? sheet.lane : undefined;
  const editingHandheld =
    handheld && editingLane
      ? data.policies.find((policy) => policy.lane === editingLane)
      : undefined;
  const handheldRevisions = handheld
    ? data.revisions.filter((item) => item.lane?.endsWith("native"))
    : [];
  const laneTitles: Record<string, string> = {
    "android-native": copy.policy.androidNative,
    "ios-native": copy.policy.handheldIosNative,
  };

  return (
    <>
      <ScreenFrame
        header={header}
        refreshing={refreshing}
        onRefresh={onRefresh}
      >
        <View style={styles.group}>
          <SectionHeader
            title={copy.download.title}
            meta={copy.download.hint}
          />
          {/* 手持以 Android 设备为主，Android 卡片放前面 */}
          {handheld ? [androidCard, storeCard] : [storeCard, androidCard]}
          {store ? <InfoNote>{copy.download.storeNote}</InfoNote> : null}
        </View>

        {data.kind === "native" ? (
          <PolicySummaryCard
            title={
              data.app === "pos-ipad"
                ? copy.policy.ipadNative
                : copy.policy.iosNative
            }
            version={data.policy.policyVersion}
            enabled={data.policy.enabled}
            mode={
              data.policy.minimumSupportedVersion
                ? copy.policy.required
                : copy.policy.optional
            }
            rows={nativeSummaryRows(data.policy, data, copy)}
            onEdit={() => setSheet({ kind: "editor" })}
            copy={copy}
            disabled={saving}
          />
        ) : (
          NATIVE_LANES.map((lane) => {
            const policy = data.policies.find((item) => item.lane === lane);
            if (!policy) return null;
            return (
              <PolicySummaryCard
                key={lane}
                title={laneTitles[lane]}
                version={policy.policyVersion}
                enabled={policy.enabled}
                mode={
                  policy.required ? copy.policy.required : copy.policy.optional
                }
                rows={handheldSummaryRows(policy, data, copy)}
                warning={
                  policy.candidateId && !policy.candidateValid
                    ? copy.policy.blocked
                    : null
                }
                onEdit={() => setSheet({ kind: "editor", lane })}
                copy={copy}
                disabled={saving}
              />
            );
          })
        )}

        <ReleaseListCard
          rows={releaseRows}
          copy={copy}
          onRegister={() => setSheet({ kind: "register" })}
          disabled={saving}
        />

        {handheld ? (
          <NavRow
            icon="clock-outline"
            title={copy.ota.revisions}
            subtitle={
              handheldRevisions.length
                ? fmt(copy.ota.revisionsLatest, {
                    version: handheldRevisions[0].policyVersion,
                    time: formatDateTime(handheldRevisions[0].createdAt),
                  })
                : copy.ota.noRevisions
            }
            onPress={() => setSheet({ kind: "revisions" })}
          />
        ) : null}
      </ScreenFrame>

      <QrShareSheet
        visible={sheet?.kind === "qr"}
        onDismiss={() => setSheet(null)}
        subtitle={appName}
        targets={
          sheet?.kind === "qr" && sheet.targets ? sheet.targets : qrTargets
        }
        initialKey={sheet?.kind === "qr" ? sheet.key : undefined}
        copy={copy}
      />
      <RegisterSheet
        visible={sheet?.kind === "register"}
        onDismiss={() => setSheet(null)}
        app={registerApp}
        appName={appName}
        latestLabel={latestLabel}
        copy={copy}
        onSubmit={(value) => onRegister(registerApp, value)}
      />
      {data.kind === "native" ? (
        <NativePolicyEditor
          visible={sheet?.kind === "editor"}
          onDismiss={() => setSheet(null)}
          title={
            data.app === "pos-ipad"
              ? copy.policy.ipadNative
              : copy.policy.iosNative
          }
          app={data.app}
          policy={data.policy}
          releases={data.releases}
          stores={data.stores}
          copy={copy}
          onSave={onSaveNative}
        />
      ) : editingHandheld ? (
        <HandheldNativeEditor
          visible={sheet?.kind === "editor"}
          onDismiss={() => setSheet(null)}
          title={laneTitles[editingHandheld.lane] ?? editingHandheld.lane}
          policy={editingHandheld}
          candidates={data.candidates.filter(
            (item) => item.lane === editingHandheld.lane,
          )}
          copy={copy}
          onSave={(form) => onSaveHandheld(editingHandheld, form)}
        />
      ) : null}
      {hasAndroid ? (
        <BuildHistorySheet
          visible={sheet?.kind === "history"}
          onDismiss={() => setSheet(null)}
          appKey={buildAppKey}
          profile={buildProfile}
          environmentLabel={environmentLabel}
          copy={copy}
          onQr={(target) => {
            // iOS 不能在一个原生弹层关闭的同一帧弹出另一个，稍等关闭完成再打开扫码弹层
            setSheet(null);
            switchTimer.current = setTimeout(
              () =>
                setSheet({ kind: "qr", key: target.key, targets: [target] }),
              350,
            );
          }}
        />
      ) : null}
      {handheld ? (
        <RevisionsSheet
          visible={sheet?.kind === "revisions"}
          onDismiss={() => setSheet(null)}
          revisions={handheldRevisions}
          laneLabels={laneTitles}
          copy={copy}
        />
      ) : null}
    </>
  );
}

const styles = StyleSheet.create({
  group: { gap: HB_SPACING.sm },
});
