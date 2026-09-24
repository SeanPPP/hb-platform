import { useEffect, useState } from "react";
import { Linking, Share, StyleSheet, View } from "react-native";
import * as Clipboard from "expo-clipboard";
import { Text } from "react-native-paper";
import QRCode from "react-native-qrcode-svg";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";
import type { Copy } from "./copy";
import { PrimaryButton, SecondaryButton, SegmentedControl, ui } from "./ui";

export function safeHttpUrl(url: string | null | undefined) {
  const trimmed = url?.trim();
  return trimmed && /^https?:\/\//i.test(trimmed) ? trimmed : null;
}

/** 打开 / 复制 / 分享三种链接操作；结果反馈交给调用方（页面 Snackbar 或弹层内提示）。 */
export function useLinkActions(copy: Copy, notify: (message: string) => void) {
  return {
    open: (url: string) =>
      void Linking.openURL(url).catch(() => notify(copy.error)),
    copy: (url: string) =>
      void Clipboard.setStringAsync(url)
        .then(() => notify(copy.actions.copied))
        .catch(() => notify(copy.error)),
    share: (url: string) =>
      void Share.share({ message: url }).catch(() => notify(copy.error)),
  };
}

export interface QrTarget {
  key: string;
  label: string;
  url: string;
  version: string;
  source: string;
  hint: string;
}

export function QrShareSheet({
  visible,
  onDismiss,
  subtitle,
  targets,
  initialKey,
  copy,
}: {
  visible: boolean;
  onDismiss: () => void;
  subtitle: string;
  targets: QrTarget[];
  initialKey?: string;
  copy: Copy;
}) {
  const [key, setKey] = useState(initialKey);
  const [status, setStatus] = useState("");
  useEffect(() => {
    if (visible) {
      setKey(initialKey);
      setStatus("");
    }
  }, [visible, initialKey]);
  const links = useLinkActions(copy, setStatus);
  const target = targets.find((item) => item.key === key) ?? targets[0];
  if (!target) return null;
  return (
    <BusinessSheet
      visible={visible}
      title={copy.qr.title}
      subtitle={subtitle}
      onDismiss={onDismiss}
      footer={
        <View style={styles.actions}>
          <SecondaryButton
            icon="content-copy"
            label={copy.actions.copy}
            onPress={() => links.copy(target.url)}
            style={styles.action}
          />
          <PrimaryButton
            icon="share-variant"
            label={copy.actions.share}
            onPress={() => links.share(target.url)}
            style={styles.action}
          />
        </View>
      }
    >
      {targets.length > 1 ? (
        <SegmentedControl
          options={targets.map((item) => ({
            value: item.key,
            label: item.label,
          }))}
          value={target.key}
          onChange={(next) => {
            setKey(next);
            setStatus("");
          }}
        />
      ) : null}
      <View style={styles.qrBox}>
        <QRCode
          value={target.url}
          size={208}
          backgroundColor="#FFFFFF"
          color="#101828"
        />
      </View>
      <View style={styles.center}>
        <Text style={styles.version}>{target.version}</Text>
        <Text style={ui.caption}>{target.source}</Text>
      </View>
      <Text selectable style={styles.url}>
        {target.url}
      </Text>
      <Text style={ui.caption}>{target.hint}</Text>
      {status ? (
        <Text accessibilityLiveRegion="polite" style={styles.status}>
          {status}
        </Text>
      ) : null}
    </BusinessSheet>
  );
}

const styles = StyleSheet.create({
  actions: { flexDirection: "row", gap: HB_SPACING.sm },
  action: { flex: 1 },
  qrBox: {
    alignSelf: "center",
    padding: 14,
    borderWidth: 1,
    borderColor: HB_COLORS.outlineMuted,
    borderRadius: HB_RADIUS.surface,
    backgroundColor: HB_COLORS.white,
  },
  center: { alignItems: "center", gap: 2 },
  version: {
    fontSize: 15,
    lineHeight: 22,
    fontWeight: "600",
    color: HB_COLORS.textPrimary,
  },
  url: {
    padding: HB_SPACING.sm,
    borderRadius: HB_RADIUS.control,
    backgroundColor: HB_COLORS.surfaceMuted,
    fontSize: 13,
    lineHeight: 18,
    color: "#344054",
  },
  status: { fontSize: 13, lineHeight: 20, color: HB_COLORS.success },
});
