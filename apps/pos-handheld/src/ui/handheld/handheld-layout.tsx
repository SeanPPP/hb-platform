import { StatusBar } from "expo-status-bar";
import type { ReactNode } from "react";
import {
  StyleSheet,
  Text,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import {
  handheldControl,
  handheldLayout,
  handheldTone,
  handheldType,
} from "./handheld-design-tokens";

import { posColors } from "@/ui/theme";

type HandheldScreenFrameProps = Readonly<{
  children: ReactNode;
  contentStyle?: StyleProp<ViewStyle>;
  footer?: ReactNode;
  header?: ReactNode;
  testID: string;
}>;

/** 竖屏任务页骨架：正文可滚动区与固定主操作区始终分离。 */
export function HandheldScreenFrame({
  children,
  contentStyle,
  footer,
  header,
  testID,
}: HandheldScreenFrameProps) {
  return (
    <SafeAreaView style={styles.safeArea} testID={testID}>
      <StatusBar style="dark" />
      {header}
      <View
        style={[styles.content, contentStyle]}
        testID={`${testID}-content`}
      >
        {children}
      </View>
      {footer ? (
        <View style={styles.footer} testID={`${testID}-footer`}>
          {footer}
        </View>
      ) : null}
    </SafeAreaView>
  );
}

type HandheldPageHeaderProps = Readonly<{
  eyebrow?: string;
  leading?: ReactNode;
  subtitle?: string;
  title: string;
  trailing?: ReactNode;
}>;

export function HandheldPageHeader({
  eyebrow,
  leading,
  subtitle,
  title,
  trailing,
}: HandheldPageHeaderProps) {
  return (
    <View style={styles.header}>
      {leading ? <View style={styles.headerEdge}>{leading}</View> : null}
      <View style={styles.headerCopy}>
        {eyebrow ? (
          <Text numberOfLines={1} style={styles.eyebrow}>
            {eyebrow}
          </Text>
        ) : null}
        <Text numberOfLines={1} style={styles.title}>
          {title}
        </Text>
        {subtitle ? (
          <Text numberOfLines={2} style={styles.subtitle}>
            {subtitle}
          </Text>
        ) : null}
      </View>
      {trailing ? <View style={styles.headerEdge}>{trailing}</View> : null}
    </View>
  );
}

type HandheldSectionProps = Readonly<{
  action?: ReactNode;
  children: ReactNode;
  testID?: string;
  title?: string;
}>;

export function HandheldSection({
  action,
  children,
  testID,
  title,
}: HandheldSectionProps) {
  return (
    <View style={styles.section} testID={testID}>
      {title || action ? (
        <View style={styles.sectionHeading}>
          {title ? <Text style={styles.sectionTitle}>{title}</Text> : <View />}
          {action}
        </View>
      ) : null}
      {children}
    </View>
  );
}

type HandheldStatusBadgeProps = Readonly<{
  label: string;
  tone?: keyof typeof handheldTone;
}>;

export function HandheldStatusBadge({
  label,
  tone = "info",
}: HandheldStatusBadgeProps) {
  const colors = handheldTone[tone];
  return (
    <View
      accessibilityRole="text"
      accessibilityValue={{ text: label }}
      style={[styles.badge, { backgroundColor: colors.background }]}
      testID="handheld-status-badge"
    >
      <View style={[styles.badgeDot, { backgroundColor: colors.foreground }]} />
      <Text style={[styles.badgeLabel, { color: colors.foreground }]}>
        {label}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    backgroundColor: posColors.canvas,
    flex: 1,
  },
  content: {
    flex: 1,
    gap: handheldLayout.sectionGap,
    paddingHorizontal: handheldLayout.screenPadding,
    paddingVertical: handheldLayout.compactGap,
  },
  footer: {
    backgroundColor: posColors.surface,
    borderTopColor: posColors.border,
    borderTopWidth: handheldControl.borderWidth,
    padding: handheldLayout.fixedActionPadding,
  },
  header: {
    alignItems: "center",
    backgroundColor: posColors.surface,
    borderBottomColor: posColors.border,
    borderBottomWidth: handheldControl.borderWidth,
    flexDirection: "row",
    gap: handheldLayout.compactGap,
    minHeight: 64,
    paddingHorizontal: handheldLayout.screenPadding,
    paddingVertical: handheldLayout.compactGap,
  },
  headerCopy: {
    flex: 1,
    minWidth: 0,
  },
  headerEdge: {
    alignItems: "center",
    justifyContent: "center",
    minHeight: handheldControl.minimumHeight,
    minWidth: handheldControl.minimumHeight,
  },
  eyebrow: {
    color: posColors.orange,
    fontSize: handheldType.metadata,
    fontWeight: "800",
    letterSpacing: 0.4,
  },
  title: {
    color: posColors.ink,
    fontSize: handheldType.title,
    fontWeight: "800",
    letterSpacing: -0.4,
    lineHeight: 30,
  },
  subtitle: {
    color: posColors.mutedInk,
    fontSize: handheldType.metadata,
    lineHeight: 17,
    marginTop: 2,
  },
  section: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: handheldControl.radius,
    borderWidth: handheldControl.borderWidth,
    gap: handheldLayout.compactGap,
    padding: handheldLayout.screenPadding,
  },
  sectionHeading: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
    minHeight: 24,
  },
  sectionTitle: {
    color: posColors.ink,
    fontSize: handheldType.sectionTitle,
    fontWeight: "800",
  },
  badge: {
    alignItems: "center",
    alignSelf: "flex-start",
    borderRadius: 12,
    flexDirection: "row",
    gap: 6,
    minHeight: 24,
    paddingHorizontal: 8,
  },
  badgeDot: {
    borderRadius: 3,
    height: 6,
    width: 6,
  },
  badgeLabel: {
    fontSize: 11,
    fontWeight: "800",
  },
});
