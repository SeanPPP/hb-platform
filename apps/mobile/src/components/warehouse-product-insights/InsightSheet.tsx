import type { ReactNode } from "react";
import {
  Modal,
  Platform,
  Pressable,
  KeyboardAvoidingView,
  StyleSheet,
  useWindowDimensions,
  View,
} from "react-native";
import { Button, Text } from "react-native-paper";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

export interface InsightSheetProps {
  visible: boolean;
  title: string;
  subtitle?: string | null;
  closeLabel: string;
  onClose: () => void;
  /** 占屏高比例；区间选择等短内容用较小比例，明细表用较大比例。 */
  heightRatio?: number;
  children: ReactNode;
}

/** 三个下钻面板共用的底部抽屉容器，保证手柄、标题栏与安全区表现一致。 */
export function InsightSheet({
  visible,
  title,
  subtitle,
  closeLabel,
  onClose,
  heightRatio = 0.86,
  children,
}: InsightSheetProps) {
  const { height } = useWindowDimensions();
  const insets = useSafeAreaInsets();
  if (!visible) return null;
  return (
    <Modal
      transparent
      visible
      animationType="slide"
      presentationStyle="overFullScreen"
      statusBarTranslucent
      onRequestClose={onClose}
    >
      <KeyboardAvoidingView
        style={styles.overlay}
        behavior={Platform.OS === "ios" ? "padding" : undefined}
      >
        <Pressable style={styles.backdrop} onPress={onClose} />
        <View
          style={[
            styles.sheet,
            {
              height: Math.min(height * heightRatio, height - insets.top),
              paddingBottom: insets.bottom,
            },
          ]}
        >
          <View style={styles.handle} />
          <View style={styles.header}>
            <View style={styles.heading}>
              <Text variant="titleMedium" style={styles.title}>
                {title}
              </Text>
              {subtitle ? (
                <Text numberOfLines={1} style={styles.subtitle}>
                  {subtitle}
                </Text>
              ) : null}
            </View>
            <Button compact onPress={onClose}>
              {closeLabel}
            </Button>
          </View>
          {children}
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  overlay: { flex: 1, justifyContent: "flex-end" },
  backdrop: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: "rgba(16,24,40,0.44)",
  },
  sheet: {
    backgroundColor: HB_COLORS.white,
    borderTopLeftRadius: HB_RADIUS.sheet,
    borderTopRightRadius: HB_RADIUS.sheet,
    overflow: "hidden",
  },
  handle: {
    width: 44,
    height: 4,
    marginTop: 10,
    marginBottom: 4,
    borderRadius: 2,
    backgroundColor: HB_COLORS.outline,
    alignSelf: "center",
  },
  header: {
    minHeight: 52,
    paddingLeft: HB_SPACING.md,
    paddingRight: HB_SPACING.xs,
    flexDirection: "row",
    alignItems: "center",
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: HB_COLORS.outlineMuted,
  },
  heading: { flex: 1, minWidth: 0 },
  title: { fontWeight: "800", color: HB_COLORS.textPrimary },
  subtitle: { color: HB_COLORS.textSecondary, fontSize: 12 },
});
