import { useState } from "react";
import { ActivityIndicator, StyleSheet, View } from "react-native";
import { useQuery } from "@tanstack/react-query";
import { IconButton, Text } from "react-native-paper";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import { HB_COLORS, HB_SPACING } from "@/shared/theme/tokens";
import { appDownloadsApi } from "./api";
import { fmt, formatDateTime, type Copy } from "./copy";
import { buildStableAndroidDownloadUrl } from "./logic";
import { safeHttpUrl, useLinkActions, type QrTarget } from "./share-sheet";
import type {
  AppBuild,
  AppDownloadAppKey,
  AppDownloadEnvironment,
} from "./types";
import {
  ActionTiles,
  FooterLinks,
  IconBadge,
  Panel,
  Pill,
  PrimaryButton,
  SecondaryButton,
  SegmentedControl,
  StatusDot,
  TextLink,
  ui,
  type Tone,
} from "./ui";

type LinkActions = ReturnType<typeof useLinkActions>;

/** App Store 下载卡片需要的展示数据；来源可以是登记版本，也可以是手持候选。 */
export interface StoreDownload {
  version: string;
  buildNumber: string | null;
  url: string | null;
  meta: string;
  region: string | null;
  active: boolean;
}

export function storeQrTarget(
  download: StoreDownload,
  label: string,
  copy: Copy,
): QrTarget | null {
  const url = safeHttpUrl(download.url);
  if (!url) return null;
  return {
    key: "ios",
    label,
    url,
    version: download.buildNumber
      ? `${download.version} (${download.buildNumber})`
      : download.version,
    source: copy.qr.iosSource,
    hint: copy.qr.iosHint,
  };
}

export function IosDownloadCard({
  label,
  download,
  copy,
  links,
  onQr,
  onRegister,
}: {
  label: string;
  download: StoreDownload | null;
  copy: Copy;
  links: LinkActions;
  onQr: () => void;
  onRegister: () => void;
}) {
  if (!download) {
    return (
      <Panel dashed>
        <View style={ui.cardBody}>
          <View style={ui.headRow}>
            <IconBadge icon="shopping-outline" tone="neutral" />
            <View style={ui.flexText}>
              <Text style={ui.cardTitle}>{label}</Text>
              <Text style={ui.caption}>{copy.download.appStore}</Text>
            </View>
          </View>
          <View>
            <Text style={ui.cardTitle}>{copy.download.iosEmptyTitle}</Text>
            <Text style={ui.muted}>{copy.download.iosEmptyBody}</Text>
          </View>
          <PrimaryButton
            icon="plus"
            label={copy.actions.register}
            onPress={onRegister}
            style={styles.startButton}
          />
        </View>
      </Panel>
    );
  }
  const url = safeHttpUrl(download.url);
  return (
    <Panel>
      <View style={ui.cardBody}>
        <View style={ui.headRow}>
          <IconBadge icon="shopping-outline" tone="brand" />
          <View style={ui.flexText}>
            <Text style={ui.cardTitle}>{label}</Text>
            <Text style={ui.caption}>
              {download.region
                ? `${copy.download.appStore} · ${download.region.toUpperCase()}`
                : copy.download.appStore}
            </Text>
          </View>
          <Pill
            label={
              download.active
                ? copy.download.activeRelease
                : copy.download.latestRegistered
            }
            tone={download.active ? "success" : "neutral"}
          />
        </View>
        <View style={ui.versionRow}>
          <Text style={ui.version}>{download.version}</Text>
          {download.buildNumber ? (
            <Text style={ui.muted}>Build {download.buildNumber}</Text>
          ) : null}
        </View>
        <Text style={ui.caption}>{download.meta}</Text>
      </View>
      <ActionTiles
        items={[
          {
            icon: "qrcode",
            label: copy.actions.qr,
            onPress: onQr,
            emphasis: true,
            disabled: !url,
          },
          {
            icon: "share-variant",
            label: copy.actions.share,
            onPress: () => url && links.share(url),
            disabled: !url,
          },
          {
            icon: "content-copy",
            label: copy.actions.copy,
            onPress: () => url && links.copy(url),
            disabled: !url,
          },
          {
            icon: "open-in-new",
            label: copy.actions.open,
            onPress: () => url && links.open(url),
            disabled: !url,
          },
        ]}
      />
    </Panel>
  );
}

export function useLatestBuild(
  appKey: AppDownloadAppKey,
  profile: AppDownloadEnvironment,
  enabled = true,
) {
  return useQuery({
    queryKey: ["app-downloads-build-latest", appKey, profile],
    queryFn: () => appDownloadsApi.getLatestBuild(appKey, profile),
    enabled,
  });
}

function mirrorState(
  build: AppBuild,
  copy: Copy,
): { tone: Tone; label: string } {
  const status = build.cosMirrorStatus?.toLowerCase();
  if (build.cosArtifactUrl || status === "succeeded")
    return { tone: "success", label: copy.download.mirrorSucceeded };
  if (status === "failed")
    return { tone: "danger", label: copy.download.mirrorFailed };
  return { tone: "warning", label: copy.download.mirrorPending };
}

/**
 * 最新构建的分享链接：有可下载产物时优先用稳定入口（每次访问都解析到最新构建），
 * 基址不可用时退回本次构建的产物地址。
 */
export function androidShareUrl(
  build: AppBuild | null | undefined,
  appKey: AppDownloadAppKey,
  profile: AppDownloadEnvironment,
  apiBaseUrl: string | undefined,
) {
  const artifactUrl = safeHttpUrl(build?.artifactUrl);
  if (!artifactUrl) return null;
  return (
    buildStableAndroidDownloadUrl(apiBaseUrl, appKey, profile) ?? artifactUrl
  );
}

export function androidQrTarget(
  build: AppBuild,
  url: string,
  environmentLabel: string,
  copy: Copy,
  hint = copy.qr.androidHint,
): QrTarget {
  return {
    key: "android",
    label: copy.platforms.android,
    url,
    version: `${build.appVersion ?? "—"} (${build.appBuildVersion ?? "—"})`,
    source: fmt(copy.qr.androidSource, { environment: environmentLabel }),
    hint,
  };
}

export function AndroidBuildCard({
  latest,
  shareUrl,
  profile,
  onProfileChange,
  copy,
  links,
  onQr,
  onHistory,
  disabled,
}: {
  latest: ReturnType<typeof useLatestBuild>;
  shareUrl: string | null;
  profile: AppDownloadEnvironment;
  onProfileChange?: (profile: AppDownloadEnvironment) => void;
  copy: Copy;
  links: LinkActions;
  onQr: () => void;
  onHistory: () => void;
  disabled?: boolean;
}) {
  const build = latest.data ?? null;
  const detailsUrl = safeHttpUrl(build?.buildDetailsPageUrl);
  const mirror = build ? mirrorState(build, copy) : null;
  return (
    <Panel>
      <View style={ui.cardBody}>
        <View style={ui.headRow}>
          <IconBadge icon="package-variant-closed" tone="success" />
          <View style={ui.flexText}>
            <Text style={ui.cardTitle}>{copy.platforms.android}</Text>
            <Text style={ui.caption}>{copy.download.apk}</Text>
          </View>
          {onProfileChange ? (
            <SegmentedControl
              options={[
                { value: "production", label: copy.environments.production },
                { value: "preview", label: copy.environments.preview },
              ]}
              value={profile}
              onChange={onProfileChange}
              disabled={disabled}
              style={styles.envSwitch}
            />
          ) : null}
        </View>
        {latest.isLoading ? (
          <ActivityIndicator color={HB_COLORS.action} style={styles.loading} />
        ) : latest.error ? (
          <View style={ui.headRow}>
            <Text style={[ui.error, ui.flexText]}>
              {copy.loadFailed}: {String(latest.error)}
            </Text>
            <TextLink
              label={copy.actions.retry}
              onPress={() => void latest.refetch()}
            />
          </View>
        ) : build ? (
          <>
            <View style={ui.versionRow}>
              <Text style={ui.version}>{build.appVersion ?? "—"}</Text>
              <Text style={ui.muted}>Build {build.appBuildVersion ?? "—"}</Text>
            </View>
            <View style={ui.statusRow}>
              {mirror ? <StatusDot tone={mirror.tone} /> : null}
              <Text style={[ui.caption, ui.flexText]}>
                {fmt(copy.download.builtAt, {
                  time: formatDateTime(build.completedAt ?? build.createdAt),
                })}
                {mirror ? ` · ${mirror.label}` : ""}
              </Text>
            </View>
            {build.cosMirrorError ? (
              <Text style={ui.error}>{build.cosMirrorError}</Text>
            ) : null}
          </>
        ) : (
          <Text style={ui.muted}>{copy.download.androidEmpty}</Text>
        )}
      </View>
      {build ? (
        <ActionTiles
          items={[
            {
              icon: "qrcode",
              label: copy.actions.qr,
              onPress: onQr,
              emphasis: true,
              disabled: !shareUrl,
            },
            {
              icon: "share-variant",
              label: copy.actions.share,
              onPress: () => shareUrl && links.share(shareUrl),
              disabled: !shareUrl,
            },
            {
              icon: "content-copy",
              label: copy.actions.copy,
              onPress: () => shareUrl && links.copy(shareUrl),
              disabled: !shareUrl,
            },
            {
              icon: "download",
              label: copy.actions.download,
              onPress: () => shareUrl && links.open(shareUrl),
              disabled: !shareUrl,
            },
          ]}
        />
      ) : null}
      <FooterLinks
        items={[
          ...(detailsUrl
            ? [
                {
                  icon: "file-document-outline",
                  label: copy.actions.buildDetails,
                  onPress: () => links.open(detailsUrl),
                },
              ]
            : []),
          {
            icon: "history",
            label: copy.actions.history,
            onPress: onHistory,
          },
        ]}
      />
    </Panel>
  );
}

/** 历史构建弹层：每行给出这一次构建自己的产物链接，二维码交给页面级扫码弹层展示。 */
export function BuildHistorySheet({
  visible,
  onDismiss,
  appKey,
  profile,
  environmentLabel,
  copy,
  onQr,
}: {
  visible: boolean;
  onDismiss: () => void;
  appKey: AppDownloadAppKey;
  profile: AppDownloadEnvironment;
  environmentLabel: string;
  copy: Copy;
  onQr: (target: QrTarget) => void;
}) {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("");
  const links = useLinkActions(copy, setStatus);
  const history = useQuery({
    queryKey: ["app-downloads-build-history", appKey, profile, page],
    queryFn: () => appDownloadsApi.getBuilds(appKey, profile, page),
    enabled: visible,
  });
  const total = Math.max(
    1,
    Math.ceil((history.data?.total ?? 0) / (history.data?.pageSize ?? 20)),
  );
  return (
    <BusinessSheet
      visible={visible}
      title={copy.history.title}
      subtitle={environmentLabel}
      onDismiss={onDismiss}
      footer={
        <View style={styles.pager}>
          <SecondaryButton
            label={copy.history.previous}
            disabled={page <= 1}
            onPress={() => setPage((value) => Math.max(1, value - 1))}
            style={styles.pagerButton}
          />
          <Text style={ui.caption}>
            {fmt(copy.history.page, {
              page: history.data?.page ?? page,
              total,
            })}
          </Text>
          <SecondaryButton
            label={copy.history.next}
            disabled={
              !history.data ||
              page * history.data.pageSize >= history.data.total
            }
            onPress={() => setPage((value) => value + 1)}
            style={styles.pagerButton}
          />
        </View>
      }
    >
      {status ? <Text style={styles.status}>{status}</Text> : null}
      {history.isLoading ? (
        <ActivityIndicator color={HB_COLORS.action} style={styles.loading} />
      ) : history.error ? (
        <Text style={ui.error}>
          {copy.loadFailed}: {String(history.error)}
        </Text>
      ) : history.data?.items.length ? (
        <Panel>
          {history.data.items.map((build, index) => {
            const url = safeHttpUrl(build.artifactUrl);
            return (
              <View
                key={build.id}
                style={[styles.historyRow, index > 0 && styles.divider]}
              >
                <View style={ui.flexText}>
                  <Text style={styles.historyTitle}>
                    {build.appVersion ?? "—"} ({build.appBuildVersion ?? "—"})
                  </Text>
                  <Text style={ui.caption}>
                    {formatDateTime(build.completedAt ?? build.createdAt)} ·{" "}
                    {build.status ?? "—"}
                  </Text>
                </View>
                <IconButton
                  icon="qrcode"
                  accessibilityLabel={copy.actions.qr}
                  iconColor={HB_COLORS.action}
                  disabled={!url}
                  onPress={() =>
                    url &&
                    onQr(
                      androidQrTarget(
                        build,
                        url,
                        environmentLabel,
                        copy,
                        copy.qr.buildHint,
                      ),
                    )
                  }
                />
                <IconButton
                  icon="content-copy"
                  accessibilityLabel={copy.actions.copy}
                  iconColor={HB_COLORS.action}
                  disabled={!url}
                  onPress={() => url && links.copy(url)}
                />
              </View>
            );
          })}
        </Panel>
      ) : (
        <Text style={ui.muted}>{copy.empty}</Text>
      )}
    </BusinessSheet>
  );
}

const styles = StyleSheet.create({
  startButton: { alignSelf: "flex-start" },
  envSwitch: { width: 132 },
  loading: { marginVertical: HB_SPACING.sm },
  pager: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: HB_SPACING.sm,
  },
  pagerButton: { minWidth: 104 },
  historyRow: {
    minHeight: 60,
    paddingLeft: HB_SPACING.md,
    paddingRight: HB_SPACING.xxs,
    flexDirection: "row",
    alignItems: "center",
  },
  historyTitle: {
    fontSize: 15,
    lineHeight: 22,
    fontWeight: "600",
    color: HB_COLORS.textPrimary,
    fontVariant: ["tabular-nums"],
  },
  divider: { borderTopWidth: 1, borderTopColor: HB_COLORS.outlineMuted },
  status: { fontSize: 13, lineHeight: 20, color: HB_COLORS.success },
});
