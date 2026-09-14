import { useMemo, useState } from "react";
import { Pressable, StyleSheet, View } from "react-native";
import { Button, IconButton, Text } from "react-native-paper";
import { BusinessSheet } from "@/components/ui/BusinessSheet";
import {
  addAvailabilityDays,
  buildAvailabilityMonthGrid,
  formatAvailabilityMonthLabel,
  getAvailabilityMonthKey,
  getAvailabilityWeek,
  normalizeAvailabilityDates,
  replaceAvailabilityWeek,
  shiftAvailabilityMonth,
  startOfAvailabilityWeek,
  toggleAvailabilityDate,
  type AvailabilityQuickSelection,
} from "@/modules/attendance/availability-dates";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";
import { HB_COLORS, HB_RADIUS, HB_SPACING } from "@/shared/theme/tokens";

export interface AvailabilityDatePickerProps {
  value: string[];
  onChange: (dates: string[]) => void;
  weekDate: string;
  onWeekChange: (date: string) => void;
  disabled?: boolean;
  singleDate?: boolean;
}

const WEEKDAY_FALLBACKS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

function formatWeekRange(week: string[]) {
  const first = week[0] ?? "";
  const last = week[6] ?? "";
  return `${first} – ${last}`;
}

function getQuickSelectionForWeek(
  selectedDates: string[],
  weekDate: string,
): AvailabilityQuickSelection | undefined {
  const week = getAvailabilityWeek(weekDate);
  const selected = new Set(selectedDates);
  const selectedInWeek = week.filter((date) => selected.has(date));
  if (selectedInWeek.length === week.length) return "all";
  if (selectedInWeek.length === 5 && week.slice(0, 5).every((date) => selected.has(date))) return "weekdays";
  if (selectedInWeek.length === 2 && week.slice(5).every((date) => selected.has(date))) return "weekend";
  return undefined;
}

export function AvailabilityDatePicker({
  value,
  onChange,
  weekDate,
  onWeekChange,
  disabled = false,
  singleDate = false,
}: AvailabilityDatePickerProps) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const selectedDates = useMemo(() => normalizeAvailabilityDates(value), [value]);
  const weekStart = startOfAvailabilityWeek(weekDate);
  const week = useMemo(() => getAvailabilityWeek(weekStart), [weekStart]);
  const [calendarVisible, setCalendarVisible] = useState(false);
  const [calendarMonth, setCalendarMonth] = useState(() => getAvailabilityMonthKey(weekDate));
  const [draftDates, setDraftDates] = useState<string[]>(selectedDates);
  const [calendarWeekDate, setCalendarWeekDate] = useState(() => startOfAvailabilityWeek(weekDate));
  const monthCells = useMemo(() => buildAvailabilityMonthGrid(calendarMonth), [calendarMonth]);
  const today = useMemo(() => getAvailabilityMonthKey(new Date()) + `-${String(new Date().getDate()).padStart(2, "0")}`, []);
  const activeQuickSelection = getQuickSelectionForWeek(selectedDates, weekStart);
  const activeCalendarSelection = getQuickSelectionForWeek(draftDates, calendarWeekDate);
  const calendarWeek = useMemo(() => getAvailabilityWeek(calendarWeekDate), [calendarWeekDate]);

  const setWeekSelection = (selection: AvailabilityQuickSelection) => {
    if (disabled || singleDate) return;
    onChange(replaceAvailabilityWeek(selectedDates, weekStart, selection));
  };

  const selectDate = (date: string) => {
    if (disabled) return;
    const next = toggleAvailabilityDate(selectedDates, date, singleDate);
    onChange(next);
    // 月历中点选某日后，快捷按钮应立即作用于该日期所在周。
    onWeekChange(startOfAvailabilityWeek(date));
  };

  const openCalendar = () => {
    if (disabled) return;
    setDraftDates(selectedDates);
    setCalendarMonth(getAvailabilityMonthKey(weekDate));
    // 月历的快捷操作沿用主卡片当前显示周；选中其他日期后再跟随点选日期切换。
    setCalendarWeekDate(weekStart);
    setCalendarVisible(true);
  };

  const selectCalendarDate = (date: string) => {
    if (disabled) return;
    setDraftDates((current) => toggleAvailabilityDate(current, date, singleDate));
    setCalendarWeekDate(startOfAvailabilityWeek(date));
  };

  const setCalendarWeekSelection = (selection: AvailabilityQuickSelection) => {
    if (disabled || singleDate) return;
    setDraftDates((current) => replaceAvailabilityWeek(current, calendarWeekDate, selection));
  };

  const confirmCalendar = () => {
    if (!disabled) {
      onChange(normalizeAvailabilityDates(draftDates));
      onWeekChange(calendarWeekDate);
    }
    setCalendarVisible(false);
  };

  return (
    <View style={styles.container}>
      <View style={styles.headingRow}>
        <View style={styles.headingText}>
          <Text variant="titleMedium" style={styles.title}>
            {t("availability.selectDates", "Select available dates")}
          </Text>
          <Text variant="bodySmall" style={styles.subtitle}>
            {t("availability.selectedCount", "{{count}} days selected", { count: selectedDates.length })}
          </Text>
        </View>
        <Button compact mode="outlined" onPress={openCalendar} disabled={disabled}>
          {t("availability.moreDates", "More dates")}
        </Button>
      </View>

      <View style={styles.weekNavigation}>
        <IconButton
          icon="chevron-left"
          accessibilityLabel={t("availability.previousWeek", "Previous week")}
          onPress={() => onWeekChange(addAvailabilityDays(weekStart, -7))}
          disabled={disabled}
          size={22}
        />
        <View style={styles.weekLabel}>
          <Text variant="labelLarge" style={styles.weekRange}>{formatWeekRange(week)}</Text>
          <Text variant="bodySmall" style={styles.muted}>{t("availability.weekStartsMonday", "Week starts Monday")}</Text>
        </View>
        <IconButton
          icon="chevron-right"
          accessibilityLabel={t("availability.nextWeek", "Next week")}
          onPress={() => onWeekChange(addAvailabilityDays(weekStart, 7))}
          disabled={disabled}
          size={22}
        />
      </View>

      {!singleDate ? (
        <View style={styles.quickActions}>
          <Button compact mode={activeQuickSelection === "all" ? "contained" : "outlined"} buttonColor={activeQuickSelection === "all" ? HB_COLORS.brand : undefined} textColor={activeQuickSelection === "all" ? HB_COLORS.white : undefined} style={styles.quickButton} onPress={() => setWeekSelection("all")} disabled={disabled}>
            {t("availability.selectWholeWeek", "Whole week")}
          </Button>
          <Button compact mode={activeQuickSelection === "weekdays" ? "contained" : "outlined"} buttonColor={activeQuickSelection === "weekdays" ? HB_COLORS.brand : undefined} textColor={activeQuickSelection === "weekdays" ? HB_COLORS.white : undefined} style={styles.quickButton} onPress={() => setWeekSelection("weekdays")} disabled={disabled}>
            {t("availability.selectWorkdays", "Workdays")}
          </Button>
          <Button compact mode={activeQuickSelection === "weekend" ? "contained" : "outlined"} buttonColor={activeQuickSelection === "weekend" ? HB_COLORS.brand : undefined} textColor={activeQuickSelection === "weekend" ? HB_COLORS.white : undefined} style={styles.quickButton} onPress={() => setWeekSelection("weekend")} disabled={disabled}>
            {t("availability.selectWeekend", "Weekend")}
          </Button>
        </View>
      ) : null}

      <View style={styles.weekGrid}>
        {week.map((date, index) => {
          const selected = selectedDates.includes(date);
          const isToday = date === today;
          const weekdayLabel = t(`weekdays.${index}`, WEEKDAY_FALLBACKS[index]);
          return (
            <Pressable
              key={date}
              accessibilityRole="checkbox"
              accessibilityLabel={`${weekdayLabel} ${date}`}
              accessibilityState={{ checked: selected, disabled }}
              disabled={disabled}
              onPress={() => selectDate(date)}
              style={({ pressed }) => [
                styles.dateCell,
                selected ? styles.selectedDateCell : null,
                isToday ? styles.todayDateCell : null,
                pressed ? styles.pressedDateCell : null,
              ]}
            >
              <Text variant="labelSmall" style={[styles.weekday, selected ? styles.selectedText : null]}>
                {weekdayLabel}
              </Text>
              <View style={[styles.checkMark, selected ? styles.checkMarkSelected : null]}>
                {selected ? <Text style={styles.checkText}>✓</Text> : null}
              </View>
              <Text variant="titleMedium" style={[styles.dayText, selected ? styles.selectedText : null]}>
                {new Date(Number(date.slice(0, 4)), Number(date.slice(5, 7)) - 1, Number(date.slice(8, 10))).getDate()}
              </Text>
            </Pressable>
          );
        })}
      </View>

      <BusinessSheet
        visible={calendarVisible}
        title={t("availability.moreDates", "More dates")}
        subtitle={t("availability.calendarHint", "Choose one or more dates")}
        onDismiss={() => setCalendarVisible(false)}
        footer={(
          <View style={styles.sheetFooter}>
            <View style={styles.sheetSummaryRow}>
              <Text variant="bodyMedium" style={styles.sheetSummary}>
                {t("availability.calendarSelected", "{{count}} days selected", { count: draftDates.length })}
              </Text>
              <Button compact onPress={() => setDraftDates([])} disabled={disabled || draftDates.length === 0}>
                {t("availability.clearDates", "Clear")}
              </Button>
            </View>
            {!singleDate ? (
              <View style={styles.calendarQuickActions}>
                <Text variant="labelMedium" style={styles.quickLabel}>
                  {t("availability.quickWeek", "Quick select {{start}} – {{end}}", { start: calendarWeek[0], end: calendarWeek[6] })}
                </Text>
                <View style={styles.quickActions}>
                  <Button compact mode={activeCalendarSelection === "all" ? "contained" : "outlined"} buttonColor={activeCalendarSelection === "all" ? HB_COLORS.brand : undefined} textColor={activeCalendarSelection === "all" ? HB_COLORS.white : undefined} style={styles.quickButton} onPress={() => setCalendarWeekSelection("all")} disabled={disabled}>
                    {t("availability.selectWholeWeek", "Whole week")}
                  </Button>
                  <Button compact mode={activeCalendarSelection === "weekdays" ? "contained" : "outlined"} buttonColor={activeCalendarSelection === "weekdays" ? HB_COLORS.brand : undefined} textColor={activeCalendarSelection === "weekdays" ? HB_COLORS.white : undefined} style={styles.quickButton} onPress={() => setCalendarWeekSelection("weekdays")} disabled={disabled}>
                    {t("availability.selectWorkdays", "Workdays")}
                  </Button>
                  <Button compact mode={activeCalendarSelection === "weekend" ? "contained" : "outlined"} buttonColor={activeCalendarSelection === "weekend" ? HB_COLORS.brand : undefined} textColor={activeCalendarSelection === "weekend" ? HB_COLORS.white : undefined} style={styles.quickButton} onPress={() => setCalendarWeekSelection("weekend")} disabled={disabled}>
                    {t("availability.selectWeekend", "Weekend")}
                  </Button>
                </View>
              </View>
            ) : null}
            <View style={styles.sheetActions}>
              <Button onPress={() => setCalendarVisible(false)}>{t("common:actions.cancel")}</Button>
              <Button mode="contained" onPress={confirmCalendar}>
                {t("availability.confirmDates", "Confirm {{count}} dates", { count: draftDates.length })}
              </Button>
            </View>
          </View>
        )}
      >
        <View style={styles.monthHeader}>
          <IconButton
            icon="chevron-left"
            accessibilityLabel={t("availability.previousMonth", "Previous month")}
            onPress={() => setCalendarMonth(shiftAvailabilityMonth(calendarMonth, -1))}
            disabled={disabled}
          />
          <Text variant="titleMedium" style={styles.monthLabel}>{formatAvailabilityMonthLabel(calendarMonth)}</Text>
          <IconButton
            icon="chevron-right"
            accessibilityLabel={t("availability.nextMonth", "Next month")}
            onPress={() => setCalendarMonth(shiftAvailabilityMonth(calendarMonth, 1))}
            disabled={disabled}
          />
        </View>
        <View style={styles.monthWeekdays}>
          {WEEKDAY_FALLBACKS.map((weekday, index) => (
            <Text key={weekday} variant="labelSmall" style={styles.monthWeekday}>
              {t(`weekdays.${index}`, weekday)}
            </Text>
          ))}
        </View>
        <View style={styles.monthGrid}>
          {monthCells.map((cell) => {
            const selected = draftDates.includes(cell.date);
            return (
              <Pressable
                key={cell.date}
                accessibilityRole="checkbox"
                accessibilityLabel={cell.date}
                accessibilityState={{ checked: selected, disabled }}
                disabled={disabled}
                onPress={() => selectCalendarDate(cell.date)}
                style={({ pressed }) => [
                  styles.monthCell,
                  !cell.isCurrentMonth ? styles.outsideMonthCell : null,
                  selected ? styles.monthCellSelected : null,
                  pressed ? styles.pressedDateCell : null,
                ]}
              >
                <Text style={[styles.monthCellText, !cell.isCurrentMonth ? styles.outsideMonthText : null, selected ? styles.selectedText : null]}>
                  {cell.day}
                </Text>
              </Pressable>
            );
          })}
        </View>
      </BusinessSheet>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { width: "100%", maxWidth: 600, gap: HB_SPACING.sm },
  headingRow: { alignItems: "center", flexDirection: "row", gap: HB_SPACING.sm },
  headingText: { flex: 1, minWidth: 0 },
  title: { color: HB_COLORS.textPrimary, fontWeight: "700" },
  subtitle: { color: HB_COLORS.textSecondary, marginTop: 2 },
  weekNavigation: { alignItems: "center", backgroundColor: HB_COLORS.surfaceMuted, borderRadius: HB_RADIUS.control, flexDirection: "row", justifyContent: "space-between", paddingHorizontal: HB_SPACING.xs },
  weekLabel: { alignItems: "center", flex: 1, minWidth: 0 },
  weekRange: { color: HB_COLORS.textPrimary },
  muted: { color: HB_COLORS.textSecondary },
  quickActions: { flexDirection: "row", gap: HB_SPACING.xs },
  quickButton: { flex: 1, minWidth: 0 },
  weekGrid: { flexDirection: "row", gap: HB_SPACING.xs },
  dateCell: { alignItems: "center", backgroundColor: HB_COLORS.white, borderColor: HB_COLORS.outlineMuted, borderRadius: HB_RADIUS.control, borderWidth: 1, flex: 1, minHeight: 84, paddingHorizontal: 2, paddingVertical: HB_SPACING.xs },
  selectedDateCell: { backgroundColor: HB_COLORS.brand, borderColor: HB_COLORS.brand },
  todayDateCell: { borderColor: HB_COLORS.action },
  pressedDateCell: { opacity: 0.72 },
  weekday: { color: HB_COLORS.textSecondary, fontSize: 10 },
  checkMark: { alignItems: "center", borderColor: HB_COLORS.outline, borderRadius: 11, borderWidth: 1, height: 22, justifyContent: "center", marginVertical: 3, width: 22 },
  checkMarkSelected: { backgroundColor: HB_COLORS.white, borderColor: HB_COLORS.white },
  checkText: { color: HB_COLORS.brand, fontSize: 14, fontWeight: "800", lineHeight: 18 },
  dayText: { color: HB_COLORS.textPrimary },
  selectedText: { color: HB_COLORS.white },
  sheetFooter: { gap: HB_SPACING.xs },
  sheetSummaryRow: { alignItems: "center", flexDirection: "row", justifyContent: "space-between" },
  sheetSummary: { color: HB_COLORS.textSecondary },
  calendarQuickActions: { gap: HB_SPACING.xxs },
  quickLabel: { color: HB_COLORS.textSecondary },
  sheetActions: { alignItems: "center", flexDirection: "row", justifyContent: "flex-end", gap: HB_SPACING.xs },
  monthHeader: { alignItems: "center", flexDirection: "row", justifyContent: "space-between" },
  monthLabel: { color: HB_COLORS.textPrimary, fontWeight: "700" },
  monthWeekdays: { flexDirection: "row", gap: HB_SPACING.xs },
  monthWeekday: { color: HB_COLORS.textSecondary, flex: 1, textAlign: "center" },
  monthGrid: { flexDirection: "row", flexWrap: "wrap" },
  monthCell: { alignItems: "center", borderRadius: HB_RADIUS.control, height: 38, justifyContent: "center", width: "14.2857%" },
  monthCellSelected: { backgroundColor: HB_COLORS.brand },
  monthCellText: { color: HB_COLORS.textPrimary },
  outsideMonthCell: { opacity: 0.42 },
  outsideMonthText: { color: HB_COLORS.textSecondary },
});
