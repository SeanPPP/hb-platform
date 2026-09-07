import { createContext, useContext, useEffect, useRef, useState, type ElementRef, type ReactNode } from "react";
import { Keyboard, StyleSheet, View } from "react-native";
import { Button, TextInput } from "react-native-paper";

type InputHandle = ElementRef<typeof TextInput>;
type Entry = { order: number; group: string; input: InputHandle };
const NumberContext = createContext<{
  register: (id: string, entry: Entry | null) => void;
  focus: (id: string) => void;
  blur: (id: string) => void;
  next: () => void;
} | null>(null);

export function CreateBatchKeyboard({ children, nextLabel, doneLabel }: { children: ReactNode; nextLabel: string; doneLabel: string }) {
  const entries = useRef(new Map<string, Entry>());
  const active = useRef("");
  const [hasNext, setHasNext] = useState(false);
  const [focused, setFocused] = useState(false);
  const [keyboardVisible, setKeyboardVisible] = useState(false);
  useEffect(() => {
    const shown = Keyboard.addListener("keyboardDidShow", () => setKeyboardVisible(true));
    const hidden = Keyboard.addListener("keyboardDidHide", () => setKeyboardVisible(false));
    return () => { shown.remove(); hidden.remove(); };
  }, []);
  const nextEntry = () => {
    const current = entries.current.get(active.current);
    return current && [...entries.current.values()].filter((entry) => entry.group === current.group && entry.order > current.order).sort((a, b) => a.order - b.order)[0];
  };
  const done = () => {
    entries.current.get(active.current)?.input.blur();
    Keyboard.dismiss();
  };
  const next = () => { const entry = nextEntry(); if (entry) entry.input.focus(); else done(); };
  return (
    <NumberContext.Provider value={{
      register(id, entry) { if (entry) entries.current.set(id, entry); else entries.current.delete(id); },
      focus(id) { active.current = id; setFocused(true); setHasNext(Boolean(nextEntry())); },
      blur(id) { if (active.current === id) { active.current = ""; setFocused(false); } },
      next,
    }}>
      <View style={styles.container}>
        {children}
        {/* 原生 Modal 中 InputAccessoryView 可能不显示；在避让后的容器底部提供可靠的键盘操作。 */}
        {focused && keyboardVisible ? <View style={styles.accessory}>
          <Button testID="creation-keyboard-next" disabled={!hasNext} contentStyle={styles.accessoryButton} textColor="#0958D9" onPress={next}>{nextLabel}</Button>
          <Button testID="creation-keyboard-done" contentStyle={styles.accessoryButton} textColor="#0958D9" onPress={done}>{doneLabel}</Button>
        </View> : null}
      </View>
    </NumberContext.Provider>
  );
}

export function CreateBatchNumberInput({ id, order, label, value, onChangeText, integer = false, group = "batch", last = false }: {
  id: string; order: number; label: string; value: string; onChangeText: (value: string) => void; integer?: boolean; group?: string; last?: boolean;
}) {
  const keyboard = useContext(NumberContext);
  const handle = useRef<InputHandle | null>(null);
  const registry = useRef(keyboard);
  registry.current = keyboard;
  useEffect(() => {
    if (handle.current) registry.current?.register(id, { order, group, input: handle.current });
    return () => registry.current?.register(id, null);
  }, [id, order, group]);
  return <TextInput
    ref={(input: InputHandle | null) => { handle.current = input; keyboard?.register(id, input ? { order, group, input } : null); }}
    testID={id}
    mode="outlined" dense label={label} value={value} onChangeText={onChangeText}
    keyboardType={integer ? "number-pad" : "decimal-pad"}
    returnKeyType={last ? "done" : "next"} blurOnSubmit={false} onSubmitEditing={() => keyboard?.next()}
    onFocus={() => keyboard?.focus(id)} onBlur={() => keyboard?.blur(id)} style={styles.input}
  />;
}

const styles = StyleSheet.create({
  container: { flex: 1, borderRadius: 12, overflow: "hidden" },
  input: { backgroundColor: "#FFFFFF", flexGrow: 1, minWidth: 110 },
  accessory: { minHeight: 44, flexDirection: "row", justifyContent: "flex-end", backgroundColor: "#F6F7F9", borderTopWidth: StyleSheet.hairlineWidth, borderColor: "#D0D5DD", paddingHorizontal: 8 },
  accessoryButton: { minHeight: 44 },
});
