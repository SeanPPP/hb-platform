export interface CameraScanGateState {
  value: string;
  timestamp: number;
  processing: boolean;
}

interface CameraScanGateOptions {
  cooldownMs: number;
  ignoreWhileProcessing: boolean;
  suppressRepeatsUntilChange: boolean;
}

export function shouldForwardCameraScan(
  state: CameraScanGateState,
  barcode: string,
  now: number,
  options: CameraScanGateOptions
) {
  if (!barcode) {
    return false;
  }

  if (options.ignoreWhileProcessing && state.processing) {
    return false;
  }

  if (state.value !== barcode) {
    return true;
  }

  // 连续相机模式下，同一条码停留在画面里不能反复触发加购/查询。
  if (options.suppressRepeatsUntilChange) {
    return false;
  }

  return now - state.timestamp >= options.cooldownMs;
}
