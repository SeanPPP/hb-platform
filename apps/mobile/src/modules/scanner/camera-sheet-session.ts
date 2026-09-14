import type { CameraScanMode } from "./use-camera-scan";

/** Native sheet 会覆盖后续业务弹层；用显式会话状态避免异步处理结束后错误复活相机。 */
export interface CameraSheetSession {
  visible: boolean;
  resumeRequested: boolean;
  /** 只在显式开启和失效时递增；迟到相机回调必须携带匹配代次。 */
  generation: number;
}

export type CameraSheetSessionEvent =
  | { type: "open" }
  | { type: "capture"; generation: number }
  | { type: "foreground-complete"; focused: boolean; generation: number }
  | { type: "dismiss" }
  | { type: "blur" };

export function createCameraSheetSession(mode: CameraScanMode): CameraSheetSession {
  void mode;
  return { visible: false, resumeRequested: false, generation: 0 };
}

/** 相机回调开始业务查询前的同步门禁，避免已关闭 sheet 的迟到事件继续写入业务状态。 */
export function isCameraSheetSessionActive(
  state: CameraSheetSession,
  generation: number,
): boolean {
  return state.visible && state.generation === generation;
}

export function reduceCameraSheetSession(
  state: CameraSheetSession,
  event: CameraSheetSessionEvent,
  mode: CameraScanMode = state.resumeRequested ? "continuous" : "single"
): CameraSheetSession {
  switch (event.type) {
    case "open":
      return {
        visible: true,
        resumeRequested: mode === "continuous",
        generation: state.generation + 1,
      };
    case "capture":
      // 关闭、失焦或前景处理中的旧回调不得重新制造可恢复的相机会话。
      if (event.generation !== state.generation || !state.visible) {
        return state;
      }
      return { ...state, visible: false, resumeRequested: mode === "continuous" };
    case "foreground-complete":
      if (event.generation !== state.generation) {
        return state;
      }
      return state.resumeRequested && event.focused && mode === "continuous"
        ? { ...state, visible: true, resumeRequested: true }
        : { ...state, visible: false, resumeRequested: false };
    case "dismiss":
    case "blur":
      return { visible: false, resumeRequested: false, generation: state.generation + 1 };
  }
}
