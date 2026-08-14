import { createContext, useContext } from "react";

/** 仅允许控件发出中性互动音；查询和购物车结果由业务层显式触发。 */
export type TouchSoundKind = "tap" | "key" | "navigate" | "danger";

export type SpecialNodeSoundCue =
  | "query-found"
  | "query-empty"
  | "query-error"
  | "cart-added"
  | "cart-incremented"
  | "cart-not-found"
  | "cart-failed-blocked";

export type PosSoundCue = TouchSoundKind | SpecialNodeSoundCue;

export type PosSoundContextValue = Readonly<{
  buttonSoundEnabled: boolean;
  specialNodeSoundEnabled: boolean;
  play(cue: PosSoundCue): void;
  setButtonSoundEnabled(enabled: boolean): void;
  setSpecialNodeSoundEnabled(enabled: boolean): void;
}>;

const NOOP_SOUND_CONTEXT: PosSoundContextValue = {
  buttonSoundEnabled: false,
  specialNodeSoundEnabled: false,
  play: () => undefined,
  setButtonSoundEnabled: () => undefined,
  setSpecialNodeSoundEnabled: () => undefined,
};

export const PosSoundContext = createContext<PosSoundContextValue>(
  NOOP_SOUND_CONTEXT,
);

/** Provider 外保持安全的关闭状态，便于独立渲染和渐进接入。 */
export function usePosSound(): PosSoundContextValue {
  return useContext(PosSoundContext);
}
