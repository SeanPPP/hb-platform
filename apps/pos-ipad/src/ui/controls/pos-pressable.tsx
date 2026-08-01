import { forwardRef, useCallback, useRef } from "react";
import {
  Pressable,
  type GestureResponderEvent,
  type PressableProps,
  type View,
} from "react-native";

import {
  type TouchSoundKind,
  usePosSound,
} from "@/ui/feedback/pos-sound-context";

export type PosPressableProps = PressableProps &
  Readonly<{
    longPressSound?: TouchSoundKind | false;
    sound?: TouchSoundKind | false;
  }>;

/** 为原生 Pressable 增加不中断业务回调的轻量触控音。 */
export const PosPressable = forwardRef<View, PosPressableProps>(
  function PosPressable(
    {
      disabled,
      longPressSound,
      onLongPress,
      onPress,
      onPressIn,
      sound = "tap",
      ...props
    },
    ref,
  ) {
    const { play } = usePosSound();
    const isDisabled = disabled === true;
    const longPressTriggered = useRef(false);
    const handlesLongPress =
      typeof onLongPress === "function" || Boolean(longPressSound);

    const handlePressIn = useCallback(
      (event: GestureResponderEvent) => {
        longPressTriggered.current = false;
        onPressIn?.(event);
      },
      [onPressIn],
    );

    const handleLongPress = useCallback(
      (event: GestureResponderEvent) => {
        longPressTriggered.current = true;
        if (!isDisabled && longPressSound) play(longPressSound);
        onLongPress?.(event);
      },
      [isDisabled, longPressSound, onLongPress, play],
    );

    const handlePress = useCallback(
      (event: GestureResponderEvent) => {
        const shouldPlay = !isDisabled && !longPressTriggered.current && sound;
        longPressTriggered.current = false;
        if (shouldPlay) play(shouldPlay);
        // 不等待音频，确保导航与业务提交维持原有时序。
        onPress?.(event);
      },
      [isDisabled, onPress, play, sound],
    );

    return (
      <Pressable
        {...props}
        {...(disabled === undefined ? {} : { disabled })}
        onLongPress={handlesLongPress ? handleLongPress : undefined}
        onPress={handlePress}
        onPressIn={handlePressIn}
        ref={ref}
      />
    );
  },
);

PosPressable.displayName = "PosPressable";
