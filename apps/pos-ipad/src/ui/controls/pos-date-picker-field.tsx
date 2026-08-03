import DateTimePicker, {
  type DateTimePickerEvent,
} from "@react-native-community/datetimepicker";
import { useState } from "react";
import {
  Keyboard,
  Modal,
  StyleSheet,
  Text,
  View,
} from "react-native";

import { posColors } from "../theme";
import { PosPressable } from "./pos-pressable";

export type PosDatePickerLocale = "en" | "zh";

export interface PosDatePickerFieldProps {
  accessibilityLabel: string;
  allowClear?: boolean;
  disabled?: boolean;
  locale: PosDatePickerLocale;
  onChange(value: string | null): void;
  testID: string;
  value: string | null;
}

const copy = {
  en: {
    anyDate: "Any date",
    cancel: "Cancel",
    clear: "Clear",
    confirm: "Confirm",
    openHint: "Opens the date picker",
    selectDate: "Select date",
  },
  zh: {
    anyDate: "不限日期",
    cancel: "取消",
    clear: "清除",
    confirm: "确定",
    openHint: "打开日期选择器",
    selectDate: "请选择日期",
  },
} as const;

const LOCAL_DATE_KEY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

function dateFromLocalKey(value: string | null): Date | null {
  const match = value?.match(LOCAL_DATE_KEY_PATTERN);
  if (!match) return null;

  const year = Number(match[1]);
  const monthIndex = Number(match[2]) - 1;
  const day = Number(match[3]);
  const date = new Date(year, monthIndex, day, 12);
  if (
    date.getFullYear() !== year ||
    date.getMonth() !== monthIndex ||
    date.getDate() !== day
  ) {
    return null;
  }
  return date;
}

function localKeyFromDate(date: Date): string {
  // 日历筛选必须使用设备本地年月日，不能经 UTC 序列化后再截断。
  const year = String(date.getFullYear()).padStart(4, "0");
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function PosDatePickerField({
  accessibilityLabel,
  allowClear = false,
  disabled = false,
  locale,
  onChange,
  testID,
  value,
}: Readonly<PosDatePickerFieldProps>) {
  const text = copy[locale];
  const [open, setOpen] = useState(false);
  const [draftDate, setDraftDate] = useState<Date>(
    () => dateFromLocalKey(value) ?? new Date(),
  );
  const displayValue = value ?? (allowClear ? text.anyDate : text.selectDate);

  const openPicker = (): void => {
    if (disabled) return;
    Keyboard.dismiss();
    setDraftDate(dateFromLocalKey(value) ?? new Date());
    setOpen(true);
  };

  const closePicker = (): void => {
    setOpen(false);
  };

  const confirm = (): void => {
    onChange(localKeyFromDate(draftDate));
    closePicker();
  };

  const clear = (): void => {
    onChange(null);
    closePicker();
  };

  const handlePickerChange = (
    _event: DateTimePickerEvent,
    nextDate?: Date,
  ): void => {
    if (nextDate && !Number.isNaN(nextDate.getTime())) {
      setDraftDate(nextDate);
    }
  };

  return (
    <>
      <PosPressable
        accessibilityHint={text.openHint}
        accessibilityLabel={accessibilityLabel}
        accessibilityRole="button"
        accessibilityState={{ disabled }}
        disabled={disabled}
        onPress={openPicker}
        style={({ pressed }) => [
          styles.trigger,
          disabled && styles.triggerDisabled,
          pressed && !disabled && styles.pressed,
        ]}
        sound="navigate"
        testID={testID}
      >
        <Text
          numberOfLines={1}
          style={[styles.triggerText, !value && styles.placeholderText]}
        >
          {displayValue}
        </Text>
        <Text accessibilityElementsHidden style={styles.disclosure}>
          ▾
        </Text>
      </PosPressable>

      {open ? (
        <Modal
          animationType="fade"
          onRequestClose={closePicker}
          presentationStyle="overFullScreen"
          statusBarTranslucent
          supportedOrientations={["landscape-left", "landscape-right"]}
          testID={`${testID}-modal`}
          transparent
          visible
        >
          <View
            accessibilityViewIsModal
            style={styles.overlay}
            testID={`${testID}-overlay`}
          >
            <View style={styles.panel}>
              <Text numberOfLines={1} style={styles.title}>
                {accessibilityLabel}
              </Text>
              <DateTimePicker
                accentColor={posColors.blue}
                display="inline"
                locale={locale === "zh" ? "zh-CN" : "en-AU"}
                mode="date"
                onChange={handlePickerChange}
                style={styles.picker}
                testID={`${testID}-picker`}
                themeVariant="light"
                value={draftDate}
              />
              <View style={styles.actions}>
                {allowClear ? (
                  <PosPressable
                    accessibilityLabel={text.clear}
                    accessibilityRole="button"
                    onPress={clear}
                    style={({ pressed }) => [
                      styles.button,
                      styles.clearButton,
                      pressed && styles.pressed,
                    ]}
                    sound="danger"
                    testID={`${testID}-clear`}
                  >
                    <Text style={styles.clearLabel}>{text.clear}</Text>
                  </PosPressable>
                ) : null}
                <PosPressable
                  accessibilityLabel={text.cancel}
                  accessibilityRole="button"
                  onPress={closePicker}
                  style={({ pressed }) => [
                    styles.button,
                    styles.cancelButton,
                    pressed && styles.pressed,
                  ]}
                  sound="tap"
                  testID={`${testID}-cancel`}
                >
                  <Text style={styles.cancelLabel}>{text.cancel}</Text>
                </PosPressable>
                <PosPressable
                  accessibilityLabel={text.confirm}
                  accessibilityRole="button"
                  onPress={confirm}
                  style={({ pressed }) => [
                    styles.button,
                    styles.confirmButton,
                    pressed && styles.pressed,
                  ]}
                  sound="navigate"
                  testID={`${testID}-confirm`}
                >
                  <Text style={styles.confirmLabel}>{text.confirm}</Text>
                </PosPressable>
              </View>
            </View>
          </View>
        </Modal>
      ) : null}
    </>
  );
}

const styles = StyleSheet.create({
  trigger: {
    alignItems: "center",
    backgroundColor: "#FFFDF8",
    borderColor: posColors.border,
    borderRadius: 8,
    borderWidth: 1,
    flexDirection: "row",
    gap: 8,
    minHeight: 44,
    minWidth: 142,
    paddingHorizontal: 12,
  },
  triggerDisabled: {
    backgroundColor: "#F1F0EC",
    opacity: 0.62,
  },
  triggerText: {
    color: posColors.ink,
    flex: 1,
    fontSize: 15,
    fontWeight: "700",
  },
  placeholderText: { color: posColors.mutedInk },
  disclosure: {
    color: posColors.blue,
    fontSize: 15,
    fontWeight: "900",
  },
  pressed: { opacity: 0.62 },
  overlay: {
    alignItems: "center",
    backgroundColor: "rgba(16, 37, 58, 0.48)",
    flex: 1,
    justifyContent: "center",
    padding: 24,
  },
  panel: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 6,
    borderWidth: 1,
    elevation: 12,
    maxWidth: "92%",
    padding: 20,
    shadowColor: "#000000",
    shadowOffset: { height: 8, width: 0 },
    shadowOpacity: 0.2,
    shadowRadius: 18,
    width: 420,
  },
  title: {
    color: posColors.ink,
    fontSize: 19,
    fontWeight: "800",
    marginBottom: 8,
  },
  picker: {
    alignSelf: "center",
    width: 360,
  },
  actions: {
    alignItems: "center",
    flexDirection: "row",
    gap: 10,
    justifyContent: "flex-end",
    marginTop: 8,
  },
  button: {
    alignItems: "center",
    justifyContent: "center",
    minHeight: 44,
    minWidth: 88,
    paddingHorizontal: 16,
  },
  clearButton: {
    backgroundColor: posColors.blueSoft,
    marginRight: "auto",
  },
  cancelButton: {
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderWidth: 1,
  },
  confirmButton: { backgroundColor: posColors.blue },
  clearLabel: { color: posColors.blue, fontSize: 15, fontWeight: "800" },
  cancelLabel: { color: posColors.ink, fontSize: 15, fontWeight: "800" },
  confirmLabel: { color: "#FFFFFF", fontSize: 15, fontWeight: "800" },
});
