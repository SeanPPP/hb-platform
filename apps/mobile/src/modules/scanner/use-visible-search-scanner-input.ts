import { useCallback, useEffect, useRef } from "react";
import { Keyboard, Platform } from "react-native";
import { extractVisibleBarcodeInput } from "./visible-barcode-input";

interface FocusableSearchInput {
  isFocused: () => boolean;
  blur: () => void;
  clear: () => void;
}

interface UseVisibleSearchScannerInputOptions {
  value: string;
  onChangeText: (value: string) => void;
  onScannerInput: (barcode: string) => void;
  burstGapMs?: number;
  now?: () => number;
}

/**
 * 可见搜索框聚焦时隐藏扫码输入框会暂停抢焦点，Zebra DataWedge 会把条码直接写进搜索框。
 * 这里把"整段写入"或"极短间隔连续写入"的纯数字条码识别为扫码：不拼接旧关键字、无需回车即走扫码入口。
 * Zebra 上搜索框聚焦时软键盘总是弹出，因此不能以软键盘是否显示区分手输与扫码，只看写入节奏。
 */
export function useVisibleSearchScannerInput<T extends FocusableSearchInput>({
  value,
  onChangeText,
  onScannerInput,
  burstGapMs = 50,
  now = Date.now,
}: UseVisibleSearchScannerInputOptions) {
  const searchInputRef = useRef<T>(null);
  const inputValueRef = useRef(value);
  const burstRef = useRef<{ baseValue: string; changes: number; lastAt: number } | null>(null);
  const burstTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const onScannerInputRef = useRef(onScannerInput);
  onScannerInputRef.current = onScannerInput;

  useEffect(() => {
    inputValueRef.current = value;
  }, [value]);

  const clearBurst = useCallback(() => {
    if (burstTimerRef.current) {
      clearTimeout(burstTimerRef.current);
      burstTimerRef.current = null;
    }
    burstRef.current = null;
  }, []);

  useEffect(() => {
    const subscription = Keyboard.addListener("keyboardDidHide", () => {
      // Zebra 收起软键盘后输入框仍可能保持焦点；失焦才会交还隐藏扫码输入框。
      if (searchInputRef.current?.isFocused()) searchInputRef.current.blur();
    });
    return () => {
      subscription.remove();
      clearBurst();
    };
  }, [clearBurst]);

  const submitScan = useCallback(
    (barcode: string) => {
      clearBurst();
      // 必须直接清空原生输入框：调用方把受控值置空时若 JS 值本就为空，RN 不会同步原生文本，
      // 条码会残留在框里，下次扫同一条码时文本不变而无法识别。
      inputValueRef.current = "";
      searchInputRef.current?.clear();
      searchInputRef.current?.blur();
      onScannerInputRef.current(barcode);
    },
    [clearBurst],
  );

  const handleChangeText = useCallback(
    (nextValue: string) => {
      const previousValue = inputValueRef.current;
      if (nextValue === previousValue) return;
      const scannerCandidate = Platform.OS === "android" && Boolean(searchInputRef.current?.isFocused());
      if (!scannerCandidate) {
        clearBurst();
        inputValueRef.current = nextValue;
        onChangeText(nextValue);
        return;
      }

      // 一次 onChangeText 整段写入条码：立即按扫码处理。
      const bulkBarcode = extractVisibleBarcodeInput(previousValue, nextValue);
      if (bulkBarcode) {
        submitScan(bulkBarcode);
        return;
      }

      // 逐字符快速写入：以极短间隔串成一次 burst，停顿后再判断整段增量是否为条码。
      const changedAt = now();
      const burst = burstRef.current;
      burstRef.current =
        burst && changedAt - burst.lastAt <= burstGapMs
          ? { baseValue: burst.baseValue, changes: burst.changes + 1, lastAt: changedAt }
          : { baseValue: previousValue, changes: 1, lastAt: changedAt };
      if (burstTimerRef.current) clearTimeout(burstTimerRef.current);
      burstTimerRef.current = setTimeout(() => {
        burstTimerRef.current = null;
        const finishedBurst = burstRef.current;
        burstRef.current = null;
        if (!finishedBurst || finishedBurst.changes < 2) return;
        const burstBarcode = extractVisibleBarcodeInput(finishedBurst.baseValue, inputValueRef.current);
        if (burstBarcode) submitScan(burstBarcode);
      }, burstGapMs);

      inputValueRef.current = nextValue;
      onChangeText(nextValue);
    },
    [burstGapMs, clearBurst, now, onChangeText, submitScan],
  );

  const blurSearchInput = useCallback(() => {
    searchInputRef.current?.blur();
  }, []);

  return { searchInputRef, handleChangeText, blurSearchInput };
}
