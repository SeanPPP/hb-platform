import {
  createContext,
  forwardRef,
  useCallback,
  useContext,
  useEffect,
  useRef,
  type ForwardedRef,
  type ReactElement,
  type Ref,
} from "react";
import {
  FlatList,
  ScrollView,
  type FlatListProps,
  type FocusEvent,
  type ScrollViewProps,
  type TextInput,
} from "react-native";

import {
  PosTextInput,
  type PosTextInputProps,
} from "@/ui/controls/pos-text-input";

const DEFAULT_KEYBOARD_REVEAL_OFFSET = 16;

type KeyboardRevealTarget = Parameters<
  ScrollView["scrollResponderScrollNativeHandleToKeyboard"]
>[0];

type RevealFocusedInput = (target: KeyboardRevealTarget) => void;

const KeyboardRevealContext = createContext<RevealFocusedInput | null>(null);

export type PosKeyboardAwareScrollViewProps = ScrollViewProps &
  Readonly<{ keyboardRevealOffset?: number }>;

export const PosKeyboardAwareScrollView = forwardRef<
  ScrollView,
  PosKeyboardAwareScrollViewProps
>(function PosKeyboardAwareScrollView(
  { keyboardRevealOffset = DEFAULT_KEYBOARD_REVEAL_OFFSET, ...props },
  forwardedRef,
) {
  const scrollViewRef = useRef<ScrollView>(null);

  const setScrollViewRef = useCallback(
    (instance: ScrollView | null) => {
      scrollViewRef.current = instance;
      if (typeof forwardedRef === "function") {
        forwardedRef(instance);
      } else if (forwardedRef) {
        forwardedRef.current = instance;
      }
    },
    [forwardedRef],
  );

  const revealFocusedInput = useCallback(
    (target: KeyboardRevealTarget) => {
      // 系统 inset 先腾出键盘空间，再把真实焦点精确滚到键盘上沿。
      scrollViewRef.current?.scrollResponderScrollNativeHandleToKeyboard(
        target,
        keyboardRevealOffset,
        true,
      );
    },
    [keyboardRevealOffset],
  );

  return (
    <KeyboardRevealContext.Provider value={revealFocusedInput}>
      <ScrollView
        {...props}
        automaticallyAdjustKeyboardInsets
        keyboardDismissMode="interactive"
        keyboardShouldPersistTaps="handled"
        ref={setScrollViewRef}
      />
    </KeyboardRevealContext.Provider>
  );
});

PosKeyboardAwareScrollView.displayName = "PosKeyboardAwareScrollView";

export type PosKeyboardAwareFlatListProps<ItemT> = FlatListProps<ItemT> &
  Readonly<{ keyboardRevealOffset?: number }>;

/** 以 PosKeyboardAwareScrollView 作为 FlatList 的滚动组件，复用固定键盘策略与聚焦揭示。 */
const PosKeyboardAwareFlatListWithRef = forwardRef(
  function PosKeyboardAwareFlatList<ItemT>(
    { keyboardRevealOffset, ...props }: PosKeyboardAwareFlatListProps<ItemT>,
    forwardedRef: ForwardedRef<FlatList<ItemT>>,
  ) {
    return (
      <FlatList
        {...props}
        ref={forwardedRef}
        renderScrollComponent={(scrollProps: ScrollViewProps) => (
          <PosKeyboardAwareScrollView
            {...scrollProps}
            {...(keyboardRevealOffset !== undefined
              ? { keyboardRevealOffset }
              : {})}
          />
        )}
      />
    );
  },
);

PosKeyboardAwareFlatListWithRef.displayName = "PosKeyboardAwareFlatList";

export const PosKeyboardAwareFlatList = PosKeyboardAwareFlatListWithRef as <
  ItemT,
>(
  props: PosKeyboardAwareFlatListProps<ItemT> & {
    ref?: Ref<FlatList<ItemT>>;
  },
) => ReactElement;

export const PosKeyboardAwareTextInput = forwardRef<
  TextInput,
  PosTextInputProps
>(function PosKeyboardAwareTextInput(
  { onBlur, onFocus, showSoftInputOnFocus, ...props },
  ref,
) {
  const revealFocusedInput = useContext(KeyboardRevealContext);
  const focusedTargetRef = useRef<KeyboardRevealTarget | null>(null);

  const handleFocus = useCallback(
    (event: FocusEvent) => {
      focusedTargetRef.current = event?.target ?? null;
      if (showSoftInputOnFocus !== false && focusedTargetRef.current != null) {
        revealFocusedInput?.(focusedTargetRef.current);
      }
      onFocus?.(event);
    },
    [onFocus, revealFocusedInput, showSoftInputOnFocus],
  );

  const handleBlur = useCallback(
    (event: FocusEvent) => {
      focusedTargetRef.current = null;
      onBlur?.(event);
    },
    [onBlur],
  );

  useEffect(() => {
    if (showSoftInputOnFocus !== true || focusedTargetRef.current == null) {
      return;
    }
    // HID 输入已聚焦时切换软键盘不会再次触发 focus，需主动揭示当前输入。
    revealFocusedInput?.(focusedTargetRef.current);
  }, [revealFocusedInput, showSoftInputOnFocus]);

  return (
    <PosTextInput
      {...props}
      onBlur={handleBlur}
      onFocus={handleFocus}
      ref={ref}
      showSoftInputOnFocus={showSoftInputOnFocus}
    />
  );
});

PosKeyboardAwareTextInput.displayName = "PosKeyboardAwareTextInput";
