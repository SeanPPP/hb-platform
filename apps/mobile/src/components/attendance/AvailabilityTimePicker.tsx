import { useEffect, useRef, useState } from "react";
import { Pressable, ScrollView, StyleSheet, View } from "react-native";
import { Button, Text } from "react-native-paper";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

export interface AvailabilityTimePickerProps {
  visible: boolean;
  title: string;
  value: string;
  onConfirm: (time: string) => void;
  onDismiss: () => void;
  caption?: string;
}

const ROW_HEIGHT = 48;
const HOURS = Array.from({ length: 24 }, (_, index) => index);
const MINUTES = Array.from({ length: 60 }, (_, index) => index);

function formatTimePart(value: number) {
  return String(value).padStart(2, "0");
}

function parseTime(value: string) {
  const match = /^(\d{1,2}):(\d{1,2})/.exec(value.trim());
  const hour = match ? Number(match[1]) : 0;
  const minute = match ? Number(match[2]) : 0;
  return {
    hour: Number.isInteger(hour) && hour >= 0 && hour <= 23 ? hour : 0,
    minute: Number.isInteger(minute) && minute >= 0 && minute <= 59 ? minute : 0,
  };
}

function formatTime(hour: number, minute: number) {
  return `${formatTimePart(hour)}:${formatTimePart(minute)}`;
}

export function AvailabilityTimePicker({
  visible,
  title,
  value,
  onConfirm,
  onDismiss,
  caption,
}: AvailabilityTimePickerProps) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const [draft, setDraft] = useState(() => parseTime(value));
  const hourScrollRef = useRef<ScrollView>(null);
  const minuteScrollRef = useRef<ScrollView>(null);

  useEffect(() => {
    if (!visible) return;
    const next = parseTime(value);
    setDraft(next);
    hourScrollRef.current?.scrollTo({ y: next.hour * ROW_HEIGHT, animated: false });
    minuteScrollRef.current?.scrollTo({ y: next.minute * ROW_HEIGHT, animated: false });
  }, [visible, value]);

  const updatePart = (part: "hour" | "minute", index: number, scroll = true) => {
    const values = part === "hour" ? HOURS : MINUTES;
    const nextIndex = Math.max(0, Math.min(values.length - 1, Math.round(index)));
    setDraft((current) => ({ ...current, [part]: nextIndex }));
    if (scroll) {
      const ref = part === "hour" ? hourScrollRef : minuteScrollRef;
      ref.current?.scrollTo({ y: nextIndex * ROW_HEIGHT, animated: true });
    }
  };

  const handleScroll = (part: "hour" | "minute", offset: number) => {
    updatePart(part, offset / ROW_HEIGHT, false);
  };

  const handleAccessibilityAction = (part: "hour" | "minute", actionName: string) => {
    if (actionName !== "increment" && actionName !== "decrement") return;
    const current = part === "hour" ? draft.hour : draft.minute;
    updatePart(part, current + (actionName === "increment" ? 1 : -1));
  };

  const renderColumn = (part: "hour" | "minute", values: number[], label: string) => {
    const selected = part === "hour" ? draft.hour : draft.minute;
    const ref = part === "hour" ? hourScrollRef : minuteScrollRef;
    return (
      <View style={styles.column}>
        <Text variant="labelMedium" style={styles.columnLabel}>{label}</Text>
        <View
          accessibilityRole="adjustable"
          accessibilityLabel={label}
          accessibilityValue={{ min: 0, max: values.length - 1, now: selected, text: formatTimePart(selected) }}
          accessibilityActions={[{ name: "increment" }, { name: "decrement" }]}
          onAccessibilityAction={(event) => handleAccessibilityAction(part, event.nativeEvent.actionName)}
          style={styles.scrollFrame}
        >
          <ScrollView
            ref={ref}
            style={styles.scroll}
            contentContainerStyle={styles.scrollContent}
            showsVerticalScrollIndicator={false}
            snapToInterval={ROW_HEIGHT}
            decelerationRate="fast"
            nestedScrollEnabled
            scrollEventThrottle={16}
            onScroll={(event) => handleScroll(part, event.nativeEvent.contentOffset.y)}
            onMomentumScrollEnd={(event) => handleScroll(part, event.nativeEvent.contentOffset.y)}
            onContentSizeChange={() => ref.current?.scrollTo({ y: selected * ROW_HEIGHT, animated: false })}
            onLayout={() => ref.current?.scrollTo({ y: selected * ROW_HEIGHT, animated: false })}
          >
            {values.map((item) => {
              const isSelected = item === selected;
              return (
                <Pressable
                  key={item}
                  accessibilityRole="button"
                  accessibilityState={{ selected: isSelected }}
                  onPress={() => updatePart(part, item)}
                  style={[styles.timeRow, isSelected ? styles.selectedTimeRow : null]}
                >
                  <Text variant="titleMedium" style={[styles.timeText, isSelected ? styles.selectedTimeText : null]}>
                    {formatTimePart(item)}
                  </Text>
                </Pressable>
              );
            })}
          </ScrollView>
        </View>
      </View>
    );
  };

  return (
    <BusinessSheet
      visible={visible}
      title={title}
      subtitle={caption}
      onDismiss={onDismiss}
      footer={(
        <View style={styles.actions}>
          <Button onPress={onDismiss}>{t("common:actions.cancel")}</Button>
          <Button mode="contained" onPress={() => onConfirm(formatTime(draft.hour, draft.minute))}>
            {t("availability.done", "Done")}
          </Button>
        </View>
      )}
    >
      <View style={styles.sheetBody}>
        <View style={styles.preview}>
          <Text variant="headlineSmall" style={styles.previewText}>{formatTime(draft.hour, draft.minute)}</Text>
          <Text variant="bodySmall" style={styles.previewCaption}>
            {caption ?? t("availability.timePicker24Hour", "24-hour time")}
          </Text>
        </View>
        <View style={styles.picker}>
          {renderColumn("hour", HOURS, t("availability.hour", "Hour"))}
          <Text variant="headlineMedium" style={styles.separator}>:</Text>
          {renderColumn("minute", MINUTES, t("availability.minute", "Minute"))}
        </View>
      </View>
    </BusinessSheet>
  );
}

const styles = StyleSheet.create({
  actions: { alignItems: "center", flexDirection: "row", justifyContent: "flex-end", gap: HB_SPACING.xs },
  sheetBody: { alignSelf: "center", maxWidth: 600, width: "100%" },
  preview: { alignItems: "center", backgroundColor: HB_COLORS.surfaceMuted, borderRadius: HB_RADIUS.control, padding: HB_SPACING.sm },
  previewText: { color: HB_COLORS.action, fontWeight: "800", letterSpacing: 1 },
  previewCaption: { color: HB_COLORS.textSecondary, marginTop: 2 },
  picker: { alignItems: "center", flexDirection: "row", gap: HB_SPACING.sm, justifyContent: "center", width: "100%" },
  column: { flex: 1, maxWidth: 260, minWidth: 0 },
  columnLabel: { color: HB_COLORS.textSecondary, marginBottom: HB_SPACING.xs, textAlign: "center" },
  scrollFrame: { borderColor: HB_COLORS.outlineMuted, borderRadius: HB_RADIUS.control, borderWidth: 1, height: 192, overflow: "hidden" },
  scroll: { flex: 1 },
  scrollContent: { paddingVertical: 72 },
  timeRow: { alignItems: "center", height: ROW_HEIGHT, justifyContent: "center", marginHorizontal: HB_SPACING.xs, borderRadius: HB_RADIUS.control },
  selectedTimeRow: { backgroundColor: HB_COLORS.brand },
  timeText: { color: HB_COLORS.textPrimary },
  selectedTimeText: { color: HB_COLORS.white, fontWeight: "800" },
  separator: { color: HB_COLORS.textSecondary, marginTop: HB_SPACING.lg },
});
