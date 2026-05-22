import { useEffect, useMemo, useState } from "react";
import { Pressable, StyleSheet, View, type StyleProp, type ViewStyle } from "react-native";
import { Card, IconButton, Text } from "react-native-paper";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

const GRID_DAYS = 42;
const DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

interface MonthDatePickerProps {
  value?: string;
  defaultValue?: string;
  disabled?: boolean;
  onChange?: (date: string) => void;
  style?: StyleProp<ViewStyle>;
}

interface MonthDatePickerCardProps extends MonthDatePickerProps {
  title?: string;
  subtitle?: string;
}

function pad2(value: number) {
  return value.toString().padStart(2, "0");
}

export function formatMonthDate(date: Date) {
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`;
}

function parseMonthDate(value?: string) {
  const match = value?.match(DATE_PATTERN);
  if (!match) {
    return undefined;
  }

  const year = Number(match[1]);
  const monthIndex = Number(match[2]) - 1;
  const day = Number(match[3]);
  const parsed = new Date(year, monthIndex, day);

  if (
    parsed.getFullYear() !== year ||
    parsed.getMonth() !== monthIndex ||
    parsed.getDate() !== day
  ) {
    return undefined;
  }

  return parsed;
}

export function normalizeMonthDate(value?: string) {
  return formatMonthDate(parseMonthDate(value) ?? new Date());
}

function getMonthStart(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

function addMonths(date: Date, count: number) {
  return new Date(date.getFullYear(), date.getMonth() + count, 1);
}

function buildMonthGrid(displayMonth: Date) {
  const monthStart = getMonthStart(displayMonth);
  const mondayOffset = (monthStart.getDay() + 6) % 7;
  const gridStart = new Date(monthStart.getFullYear(), monthStart.getMonth(), 1 - mondayOffset);

  return Array.from({ length: GRID_DAYS }, (_, index) => {
    const date = new Date(gridStart.getFullYear(), gridStart.getMonth(), gridStart.getDate() + index);
    return {
      date,
      dateString: formatMonthDate(date),
      isCurrentMonth: date.getMonth() === monthStart.getMonth(),
    };
  });
}

function chunkWeeks<T>(items: T[]) {
  return Array.from({ length: Math.ceil(items.length / 7) }, (_, index) => items.slice(index * 7, index * 7 + 7));
}

export function MonthDatePicker({
  value,
  defaultValue,
  disabled = false,
  onChange,
  style,
}: MonthDatePickerProps) {
  const { t } = useAppTranslation(["attendance", "common"]);
  const isControlled = value !== undefined;
  const [internalValue, setInternalValue] = useState(() => normalizeMonthDate(defaultValue));
  const selectedDateString = isControlled ? normalizeMonthDate(value) : internalValue;
  const selectedDate = parseMonthDate(selectedDateString) ?? new Date();
  const [displayMonth, setDisplayMonth] = useState(() => getMonthStart(selectedDate));

  useEffect(() => {
    if (isControlled) {
      setDisplayMonth(getMonthStart(parseMonthDate(value) ?? new Date()));
    }
  }, [isControlled, value]);

  const today = useMemo(() => formatMonthDate(new Date()), []);
  const monthCells = useMemo(() => buildMonthGrid(displayMonth), [displayMonth]);
  const weeks = useMemo(() => chunkWeeks(monthCells), [monthCells]);
  const monthTitle = `${displayMonth.getFullYear()}-${pad2(displayMonth.getMonth() + 1)}`;

  const selectDate = (date: Date) => {
    if (disabled) {
      return;
    }

    const nextValue = formatMonthDate(date);
    if (!isControlled) {
      setInternalValue(nextValue);
    }
    setDisplayMonth(getMonthStart(date));
    onChange?.(nextValue);
  };

  return (
    <View style={[styles.container, style]}>
      <View style={styles.monthHeader}>
        <IconButton
          icon="chevron-left"
          size={22}
          onPress={() => setDisplayMonth((current) => addMonths(current, -1))}
          disabled={disabled}
        />
        <Text variant="titleMedium" style={styles.monthTitle}>
          {monthTitle}
        </Text>
        <IconButton
          icon="chevron-right"
          size={22}
          onPress={() => setDisplayMonth((current) => addMonths(current, 1))}
          disabled={disabled}
        />
      </View>

      <View style={styles.weekdayRow}>
        {Array.from({ length: 7 }, (_, index) => (
          <Text key={index} variant="labelSmall" style={styles.weekdayText}>
            {t(`weekdays.${index}`, ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"][index])}
          </Text>
        ))}
      </View>

      <View style={styles.grid}>
        {weeks.map((week, weekIndex) => (
          <View key={weekIndex} style={styles.dateRow}>
            {week.map((cell) => {
              const isSelected = cell.dateString === selectedDateString;
              const isToday = cell.dateString === today;
              return (
                <Pressable
                  key={cell.dateString}
                  accessibilityRole="button"
                  accessibilityState={{ disabled, selected: isSelected }}
                  disabled={disabled}
                  onPress={() => selectDate(cell.date)}
                  style={({ pressed }) => [
                    styles.dateCell,
                    !cell.isCurrentMonth ? styles.outsideDateCell : null,
                    isToday ? styles.todayCell : null,
                    isSelected ? styles.selectedDateCell : null,
                    pressed && !disabled ? styles.pressedDateCell : null,
                    disabled ? styles.disabledDateCell : null,
                  ]}
                >
                  <Text
                    variant="labelLarge"
                    style={[
                      styles.dateText,
                      !cell.isCurrentMonth ? styles.outsideDateText : null,
                      isSelected ? styles.selectedDateText : null,
                    ]}
                  >
                    {cell.date.getDate()}
                  </Text>
                </Pressable>
              );
            })}
          </View>
        ))}
      </View>
    </View>
  );
}

export function MonthDatePickerCard({ title, subtitle, ...pickerProps }: MonthDatePickerCardProps) {
  const { t } = useAppTranslation(["attendance", "common"]);

  return (
    <Card mode="elevated" style={styles.card}>
      <Card.Title title={title ?? t("fields.workDate")} subtitle={subtitle} />
      <Card.Content>
        <MonthDatePicker {...pickerProps} />
      </Card.Content>
    </Card>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: 8,
  },
  container: {
    gap: 8,
  },
  dateCell: {
    alignItems: "center",
    aspectRatio: 1,
    borderColor: "transparent",
    borderRadius: 6,
    borderWidth: StyleSheet.hairlineWidth,
    flex: 1,
    justifyContent: "center",
    minHeight: 36,
  },
  dateRow: {
    flexDirection: "row",
    gap: 4,
  },
  dateText: {
    color: "#111827",
  },
  disabledDateCell: {
    opacity: 0.5,
  },
  grid: {
    gap: 4,
  },
  monthHeader: {
    alignItems: "center",
    flexDirection: "row",
    justifyContent: "space-between",
  },
  monthTitle: {
    flex: 1,
    textAlign: "center",
  },
  outsideDateCell: {
    backgroundColor: "#F9FAFB",
  },
  outsideDateText: {
    color: "#9CA3AF",
  },
  pressedDateCell: {
    backgroundColor: "#E0F2FE",
  },
  selectedDateCell: {
    backgroundColor: "#2563EB",
    borderColor: "#2563EB",
  },
  selectedDateText: {
    color: "#FFFFFF",
  },
  todayCell: {
    borderColor: "#22C55E",
  },
  weekdayRow: {
    flexDirection: "row",
    gap: 4,
  },
  weekdayText: {
    color: "#6B7280",
    flex: 1,
    textAlign: "center",
  },
});
