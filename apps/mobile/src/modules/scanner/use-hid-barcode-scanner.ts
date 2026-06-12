import { useCallback, useEffect, useRef, useState } from "react";
import { AppState } from "react-native";
import type { NativeSyntheticEvent, TextInputSubmitEditingEventData, TextInput } from "react-native";
import { getItemAsync, setItemAsync } from "expo-secure-store";
import { createHiddenInputFocusController, type HiddenInputFocusController } from "./hid-hidden-input-focus";

interface UseHidBarcodeScannerOptions {
  enabled?: boolean;
  idleMs?: number;
  minLength?: number;
  onScan: (barcode: string) => void | Promise<void>;
}

const STORAGE_KEY = "hid_scanner_force_text_input";

let nativeModuleRef: any = null;
let cachedForceTextInput: boolean | null = null;

function getNativeModule() {
  if (nativeModuleRef !== null) {
    return nativeModuleRef;
  }
  try {
    const { requireNativeModule } = require("expo-modules-core") as typeof import("expo-modules-core");
    nativeModuleRef = requireNativeModule("ExpoKeyEvent");
  } catch {
    nativeModuleRef = undefined;
  }
  return nativeModuleRef;
}

async function loadPersistedMode(): Promise<boolean | null> {
  if (cachedForceTextInput !== null) {
    return cachedForceTextInput;
  }
  try {
    const value = await getItemAsync(STORAGE_KEY);
    if (value === "true") {
      cachedForceTextInput = true;
      return true;
    }
    if (value === "false") {
      cachedForceTextInput = false;
      return false;
    }
  } catch {
    // ignore
  }
  return null;
}

async function persistForceTextInput(value: boolean) {
  cachedForceTextInput = value;
  try {
    await setItemAsync(STORAGE_KEY, value ? "true" : "false");
  } catch {
    // ignore
  }
}

export function getHidScannerAvailability() {
  return getNativeModule() != null;
}

function normalizeBarcode(rawValue: string) {
  return rawValue.replace(/[\r\n\t]/g, "").trim();
}

export function useHidBarcodeScanner({
  enabled = true,
  idleMs = 50,
  minLength = 3,
  onScan,
}: UseHidBarcodeScannerOptions) {
  const onScanRef = useRef(onScan);
  onScanRef.current = onScan;

  const [textInputValue, setTextInputValue] = useState("");
  const textInputTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const textInputRef = useRef<TextInput>(null);
  const focusTargetRef = useRef<() => void>(() => {});
  const focusControllerRef = useRef<HiddenInputFocusController | null>(null);

  const [forceTextInput, setForceTextInput] = useState<boolean>(cachedForceTextInput === true);
  const lastSubmittedRef = useRef("");
  const lastSubmittedTimeRef = useRef(0);

  // 隐藏输入框只在扫码场景抢焦点，避免覆盖用户正在编辑的可见输入框。
  focusTargetRef.current = () => {
    if (enabled) {
      textInputRef.current?.focus();
    }
  };

  if (!focusControllerRef.current) {
    focusControllerRef.current = createHiddenInputFocusController(() => focusTargetRef.current());
  }

  const submitBarcode = useCallback(
    (rawValue: string) => {
      const barcode = normalizeBarcode(rawValue);
      if (!barcode || barcode.length < minLength) {
        return;
      }

      const now = Date.now();
      if (barcode === lastSubmittedRef.current && now - lastSubmittedTimeRef.current < 2000) {
        return;
      }
      lastSubmittedRef.current = barcode;
      lastSubmittedTimeRef.current = now;

      onScanRef.current?.(barcode);
    },
    [minLength],
  );

  const handleTextInputChange = useCallback(
    (nextValue: string) => {
      setTextInputValue(nextValue);
      if (textInputTimerRef.current) {
        clearTimeout(textInputTimerRef.current);
      }
      textInputTimerRef.current = setTimeout(() => {
        submitBarcode(nextValue);
        setTextInputValue("");
      }, idleMs);
    },
    [idleMs, submitBarcode],
  );

  const handleTextInputSubmit = useCallback(
    (event: NativeSyntheticEvent<TextInputSubmitEditingEventData>) => {
      if (textInputTimerRef.current) {
        clearTimeout(textInputTimerRef.current);
        textInputTimerRef.current = null;
      }
      const value = event.nativeEvent.text || textInputValue;
      submitBarcode(value);
      setTextInputValue("");
    },
    [submitBarcode, textInputValue],
  );

  const focusHiddenInput = useCallback(() => {
    focusControllerRef.current?.focusIfAllowed();
  }, []);

  const pauseHiddenInputFocus = useCallback(() => {
    focusControllerRef.current?.pauseHiddenInputFocus();
  }, []);

  const resumeHiddenInputFocus = useCallback((options?: { refocus?: boolean }) => {
    focusControllerRef.current?.resumeHiddenInputFocus(options);
  }, []);

  useEffect(() => {
    if (cachedForceTextInput === null) {
      loadPersistedMode().then((persisted) => {
        if (persisted === true) {
          setForceTextInput(true);
        }
      });
    }
  }, []);

  useEffect(() => {
    if (!enabled) {
      return;
    }

    const interval = setInterval(() => {
      focusHiddenInput();
    }, 3000);

    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") {
        setTimeout(() => focusHiddenInput(), 100);
      }
    });

    return () => {
      clearInterval(interval);
      subscription.remove();
    };
  }, [enabled, focusHiddenInput]);

  useEffect(() => {
    if (forceTextInput && enabled) {
      setTimeout(() => focusHiddenInput(), 50);
    }
  }, [forceTextInput, enabled, focusHiddenInput]);

  useEffect(() => {
    return () => {
      if (textInputTimerRef.current) {
        clearTimeout(textInputTimerRef.current);
      }
    };
  }, []);

  useEffect(() => {
    const mod = getNativeModule();
    if (!mod) {
      return;
    }

    const onKeyPress = (event: { key: string; character?: string }) => {
      if (!enabled) {
        return;
      }

      const character = event.character || "";
      const key = event.key || "";

      if (/^\d+$/.test(key) && !character) {
        if (cachedForceTextInput !== true) {
          console.log("[HID-Scanner] scanner uses unmapped keyCodes, switching to TextInput mode");
          void persistForceTextInput(true);
          setForceTextInput(true);
        }
        return;
      }
    };

    mod.addListener("onKeyPress", onKeyPress);

    return () => {
      mod.removeListener("onKeyPress", onKeyPress);
    };
  }, [enabled]);

  useEffect(() => {
    const mod = getNativeModule();
    if (!mod) {
      return;
    }

    const useNativeMode = !forceTextInput;
    if (useNativeMode && enabled) {
      mod.startListening();
    }

    return () => {
      try {
        mod.stopListening();
      } catch {
        // ignore
      }
    };
  }, [forceTextInput, enabled]);

  if (getNativeModule() && !forceTextInput) {
    return {
      mode: "native" as const,
      textInputProps: null,
      focusHiddenInput: null,
      pauseHiddenInputFocus,
      resumeHiddenInputFocus,
    };
  }

  return {
    mode: "textInput" as const,
    textInputProps: {
      ref: textInputRef,
      value: textInputValue,
      onChangeText: handleTextInputChange,
      onSubmitEditing: handleTextInputSubmit,
      autoCapitalize: "none" as const,
      autoCorrect: false,
      blurOnSubmit: false,
      showSoftInputOnFocus: false,
      caretHidden: true,
      contextMenuHidden: true,
    },
    focusHiddenInput,
    pauseHiddenInputFocus,
    resumeHiddenInputFocus,
  };
}
