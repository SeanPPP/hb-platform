export interface CameraScanGateState {
  value: string;
  timestamp: number;
  processing: boolean;
}

export type CameraScanResetKey = string | number | boolean | null;

export interface CameraScanGateOptions {
  cooldownMs: number;
  ignoreWhileProcessing: boolean;
  singleScanUntilReset?: boolean;
  suppressRepeatsUntilChange: boolean;
}

export interface CameraScanGateLease {
  readonly state: CameraScanGateState;
}

export interface CameraScanGateController {
  setCurrentResetKey(resetKey: CameraScanResetKey): void;
  tryStart(
    callbackResetKey: CameraScanResetKey,
    barcode: string,
    now: number,
    options: CameraScanGateOptions
  ): CameraScanGateLease | null;
  finish(lease: CameraScanGateLease): void;
}

function createInitialCameraScanGateState(): CameraScanGateState {
  return {
    value: "",
    timestamp: 0,
    processing: false,
  };
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

  // 考勤会话只允许首次扫码，后续任何轮换 token 都等待 resetKey 重置。
  if (options.singleScanUntilReset && state.value) {
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

export function createCameraScanGateController(
  initialResetKey: CameraScanResetKey
): CameraScanGateController {
  let currentResetKey = initialResetKey;
  let gateResetKey = initialResetKey;
  let state = createInitialCameraScanGateState();

  return {
    setCurrentResetKey(resetKey) {
      // render 先登记最新会话，队列中的旧 callback 只能被拒绝，不能回滚 gate。
      currentResetKey = resetKey;
    },

    tryStart(callbackResetKey, barcode, now, options) {
      if (!Object.is(callbackResetKey, currentResetKey)) {
        return null;
      }

      // 新会话首码在判断前同步获得空 gate，不等待 effect。
      if (!Object.is(gateResetKey, callbackResetKey)) {
        gateResetKey = callbackResetKey;
        state = createInitialCameraScanGateState();
      }

      if (!shouldForwardCameraScan(state, barcode, now, options)) {
        return null;
      }

      const activeState: CameraScanGateState = {
        value: barcode,
        timestamp: now,
        processing: true,
      };
      state = activeState;
      return { state: activeState };
    },

    finish(lease) {
      // 仅当前 lease 能释放 processing，旧异步请求不得干扰新会话。
      if (state === lease.state) {
        state.processing = false;
      }
    },
  };
}
