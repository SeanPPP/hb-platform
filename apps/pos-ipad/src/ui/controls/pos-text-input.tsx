import { forwardRef, useCallback, useRef } from "react";
import {
  TextInput,
  type GestureResponderEvent,
  type TextInputProps,
} from "react-native";

import {
  type TouchSoundKind,
  usePosSound,
} from "@/ui/feedback/pos-sound-context";

export type PosTextInputProps = TextInputProps &
  Readonly<{ sound?: TouchSoundKind | false }>;

/** 仅手指轻触输入框时播放；程序焦点、受控 value 与 HID 输入保持静默。 */
export const PosTextInput = forwardRef<TextInput, PosTextInputProps>(
  function PosTextInput(
    {
      editable = true,
      onTouchCancel,
      onTouchEnd,
      onTouchMove,
      onTouchStart,
      sound = "key",
      ...props
    },
    ref,
  ) {
    const { play } = usePosSound();
    const touch = useRef({ active: false, moved: false });

    const handleTouchStart = useCallback(
      (event: GestureResponderEvent) => {
        touch.current = { active: true, moved: false };
        onTouchStart?.(event);
      },
      [onTouchStart],
    );
    const handleTouchMove = useCallback(
      (event: GestureResponderEvent) => {
        touch.current.moved = true;
        onTouchMove?.(event);
      },
      [onTouchMove],
    );
    const handleTouchCancel = useCallback(
      (event: GestureResponderEvent) => {
        touch.current.active = false;
        onTouchCancel?.(event);
      },
      [onTouchCancel],
    );
    const handleTouchEnd = useCallback(
      (event: GestureResponderEvent) => {
        const shouldPlay =
          editable && touch.current.active && !touch.current.moved && sound;
        touch.current.active = false;
        if (shouldPlay) play(shouldPlay);
        onTouchEnd?.(event);
      },
      [editable, onTouchEnd, play, sound],
    );

    return (
      <TextInput
        {...props}
        editable={editable}
        onTouchCancel={handleTouchCancel}
        onTouchEnd={handleTouchEnd}
        onTouchMove={handleTouchMove}
        onTouchStart={handleTouchStart}
        ref={ref}
      />
    );
  },
);

PosTextInput.displayName = "PosTextInput";
