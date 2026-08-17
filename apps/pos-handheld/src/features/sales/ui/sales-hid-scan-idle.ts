/**
 * 销售页 HID 无回车扫码节奏状态机（纯函数，无 React/定时器副作用）。
 *
 * 与通用 PosKeyboardAwareTextInput 的 autoSubmitOnScanIdle 解耦：
 * - 相邻字符间隔 <=60ms 记为快速间隔；
 * - 连续 3 个快速间隔（第 4 个字符起）确认本次输入为 HID；
 * - 确认后由销售页在最后一个字符后 85ms 提交追加条码，而非通用 180ms。
 */

export const SALES_HID_FAST_GAP_MS = 60;
export const SALES_HID_SUBMIT_IDLE_MS = 85;
export const SALES_HID_RAPID_STREAK_MIN = 3;
export const SALES_DATAWEDGE_BATCH_MIN_CHARS = 6;

export type SalesHidScanIdleState = Readonly<{
  /** 本次快速输入开始前的手动查询草稿，提交后需要恢复。 */
  baseline: string;
  /** 上一次 onChangeText 收到的完整值。 */
  previousValue: string;
  rapidStreak: number;
  confirmed: boolean;
  /** 确认 HID 后需要提交的追加条码（value.slice(baseline.length)）。 */
  pendingCode: string | null;
  lastChangeAt: number;
}>;

export type SalesHidScanChange = Readonly<{
  value: string;
  nowMs: number;
  /** Android DataWedge 可把整段扫码结果作为一次字符串变化写入当前输入框。 */
  dataWedgeBatch?: boolean;
}>;

export function createInitialSalesHidScanState(
  initialValue: string,
): SalesHidScanIdleState {
  return {
    baseline: initialValue,
    previousValue: initialValue,
    rapidStreak: 0,
    confirmed: false,
    pendingCode: null,
    lastChangeAt: 0,
  };
}

export function reduceSalesHidScanChange(
  state: SalesHidScanIdleState,
  change: SalesHidScanChange,
): SalesHidScanIdleState {
  const { dataWedgeBatch = false, value, nowMs } = change;
  const previousValue = state.previousValue;
  const gap =
    state.lastChangeAt === 0
      ? Number.POSITIVE_INFINITY
      : nowMs - state.lastChangeAt;

  // 删除、清空、非尾部插入或整段替换都不会是 HID 追加；以当前值为新的基线。
  if (value.length <= previousValue.length || !value.startsWith(previousValue)) {
    return {
      baseline: value,
      previousValue: value,
      rapidStreak: 0,
      confirmed: false,
      pendingCode: null,
      lastChangeAt: nowMs,
    };
  }

  const appendedValue = value.slice(previousValue.length);
  if (
    dataWedgeBatch &&
    appendedValue.length >= SALES_DATAWEDGE_BATCH_MIN_CHARS
  ) {
    // DataWedge 默认可关闭“字符作为事件发送”，此时 RN 只收到一次整串变化。
    // 变化前的完整值就是手动草稿，不能沿用慢速分支中滞后一位的 baseline。
    return {
      baseline: previousValue,
      previousValue: value,
      rapidStreak: 0,
      confirmed: true,
      pendingCode: appendedValue,
      lastChangeAt: nowMs,
    };
  }

  const isFast = gap <= SALES_HID_FAST_GAP_MS;
  if (isFast) {
    const rapidStreak = state.rapidStreak + 1;
    const confirmed =
      rapidStreak >= SALES_HID_RAPID_STREAK_MIN || state.confirmed;
    return {
      baseline: state.baseline,
      previousValue: value,
      rapidStreak,
      confirmed,
      pendingCode: confirmed ? value.slice(state.baseline.length) : null,
      lastChangeAt: nowMs,
    };
  }

  // 间隔过大：上一字符是本次输入的上一个字符。先以旧值暂作基线；
  // 若后续没有快速间隔，下一次慢速输入会把基线推进到当前值，保持草稿跟踪。
  return {
    baseline: previousValue,
    previousValue: value,
    rapidStreak: 0,
    confirmed: false,
    pendingCode: null,
    lastChangeAt: nowMs,
  };
}
