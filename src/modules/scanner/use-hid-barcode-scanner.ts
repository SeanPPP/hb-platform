import { useCallback, useRef } from "react";
import type { KeyPressEvent, KeyReleaseEvent } from "expo-key-event";

type KeyEvent = KeyPressEvent | KeyReleaseEvent;

interface UseHidBarcodeScannerOptions {
  enabled?: boolean;
  idleMs?: number;
  minLength?: number;
  onScan: (barcode: string) => void | Promise<void>;
}

let nativeModuleAvailable: boolean | null = null;

function checkNativeModule(): boolean {
  if (nativeModuleAvailable !== null) {
    return nativeModuleAvailable;
  }

  try {
    const { NativeModules } = require("react-native");
    nativeModuleAvailable = Boolean(
      NativeModules.ExpoKeyEventModule ?? NativeModules.ExpoKeyEvent
    );
  } catch {
    nativeModuleAvailable = false;
  }

  return nativeModuleAvailable;
}

export function getHidScannerAvailability() {
  return checkNativeModule();
}

function resolveCharacter(event: KeyEvent): string | null {
  if (event.eventType !== "press") {
    return null;
  }

  if (event.ctrlKey || event.metaKey || event.altKey) {
    return null;
  }

  const ch = event.character ?? event.key;
  if (!ch) {
    return null;
  }

  if (ch === "Enter") {
    return "ENTER";
  }

  if (ch === "Escape") {
    return "ESCAPE";
  }

  if (ch.length === 1) {
    return ch;
  }

  return null;
}

export function useHidBarcodeScanner({
  enabled = true,
  idleMs = 150,
  minLength = 3,
  onScan,
}: UseHidBarcodeScannerOptions) {
  const bufferRef = useRef("");
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const onScanRef = useRef(onScan);
  onScanRef.current = onScan;

  const flush = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }

    const value = bufferRef.current.trim();
    bufferRef.current = "";

    if (value.length >= minLength) {
      setTimeout(() => onScanRef.current?.(value), 0);
    }
  }, [minLength]);

  const scheduleFlush = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
    }
    timerRef.current = setTimeout(flush, idleMs);
  }, [flush, idleMs]);

  const handleKeyEvent = useCallback(
    (event: KeyEvent) => {
      if (!enabled) {
        return;
      }

      const ch = resolveCharacter(event);
      if (ch === null) {
        return;
      }

      if (ch === "ESCAPE") {
        bufferRef.current = "";
        if (timerRef.current) {
          clearTimeout(timerRef.current);
          timerRef.current = null;
        }
        return;
      }

      if (ch === "ENTER") {
        flush();
        return;
      }

      bufferRef.current += ch;
      scheduleFlush();
    },
    [enabled, flush, scheduleFlush],
  );

  if (checkNativeModule()) {
    const { useKeyEventListener } = require("expo-key-event") as typeof import("expo-key-event");
    useKeyEventListener(handleKeyEvent, {
      listenOnMount: enabled,
      preventReload: true,
    });
  }
}
