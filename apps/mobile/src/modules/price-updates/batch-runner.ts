import type { PriceUpdateApplyItem, PriceUpdateBatchResult, StorePriceUpdateTask } from "./types";

export type PriceUpdateRunMode = "applyOnly" | "applyAndPrint";
export type PriceUpdateRunPhase = "applying" | "printing" | "done";
export type PriceUpdatePrintStatus = "pending" | "printing" | "printed" | "failed" | "skipped";

export interface PriceUpdatePrintItem {
  taskId: number;
  /** 打印用的任务快照：改价成功后优先用接口回传的最新任务（价格已是更新后的）。 */
  task: StorePriceUpdateTask;
  status: PriceUpdatePrintStatus;
  /**
   * 已出纸但回写「已打印」失败。重试时只补回写，绝不能再打一张。
   */
  printedAwaitingConfirm: boolean;
  error?: string;
}

export interface PriceUpdateApplySummary {
  /** 提交改价的任务数（kind=PriceUpdate）。 */
  total: number;
  success: number;
  failed: number;
  /** 仓库价格已再次变化（target_changed），需要刷新列表后重新确认。 */
  targetChanged: number;
  /** 整个改价请求失败（网络等）时的错误信息。 */
  error?: string;
}

export interface PriceUpdateRunState {
  mode: PriceUpdateRunMode;
  phase: PriceUpdateRunPhase;
  apply: PriceUpdateApplySummary;
  hqSyncEnabled: boolean;
  hqSyncSubmittedCount: number;
  prints: PriceUpdatePrintItem[];
  /** 想打印但标签机不可用：只执行了改价。 */
  printerUnavailable: boolean;
  stopped: boolean;
}

export interface PriceUpdateRunDependencies {
  apply: (items: PriceUpdateApplyItem[]) => Promise<PriceUpdateBatchResult>;
  printLabel: (task: StorePriceUpdateTask) => Promise<void>;
  /** 每打成功一张立刻回写该任务为 Printed。 */
  markPrinted: (taskId: number) => Promise<void>;
  /** 同一台蓝牙标签机同时只能有一个打印流程。 */
  runPrintExclusive: <T>(operation: () => Promise<T>) => Promise<T>;
}

function describeError(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}

/** expected* 传列表里看到的 target* 原值，后端据此做乐观并发校验。 */
export function buildApplyItems(tasks: readonly StorePriceUpdateTask[]): PriceUpdateApplyItem[] {
  return tasks
    .filter((task) => task.kind === "PriceUpdate")
    .map((task) => ({
      taskId: task.id,
      expectedTargetRetailPrice: task.targetRetailPrice,
      expectedTargetDiscountRate: task.targetDiscountRate,
    }));
}

export function summarizeApplyResult(
  requestedCount: number,
  result: PriceUpdateBatchResult | null
): PriceUpdateApplySummary {
  if (!result) {
    return { total: requestedCount, success: 0, failed: requestedCount, targetChanged: 0 };
  }
  const success = result.items.filter((item) => item.success).length;
  const targetChanged = result.items.filter((item) => !item.success && item.code === "target_changed").length;
  return {
    total: requestedCount,
    success,
    // 后端漏回的条目也算失败，保证 成功 + 失败 = 提交数。
    failed: Math.max(0, requestedCount - success),
    targetChanged,
  };
}

/**
 * 需要出标签的任务 = 原本就是待换标签的 + 改价成功且仍待换标签的。
 * 改价后直接完成（价格已一致）或已不在未完成状态的不打印；改价失败的留在「需改价」。
 */
export function resolvePrintQueue(
  tasks: readonly StorePriceUpdateTask[],
  applyResult: PriceUpdateBatchResult | null
): PriceUpdatePrintItem[] {
  const applied = new Map((applyResult?.items ?? []).map((item) => [item.taskId, item]));
  const queue: PriceUpdatePrintItem[] = [];
  for (const task of tasks) {
    if (task.kind === "LabelOnly") {
      queue.push({ taskId: task.id, task, status: "pending", printedAwaitingConfirm: false });
      continue;
    }
    const outcome = applied.get(task.id);
    if (!outcome?.success) {
      continue;
    }
    const latest = outcome.task;
    if (latest && (latest.status !== "Pending" || latest.kind !== "LabelOnly")) {
      continue;
    }
    queue.push({ taskId: task.id, task: latest ?? task, status: "pending", printedAwaitingConfirm: false });
  }
  return queue;
}

function replacePrintItem(
  state: PriceUpdateRunState,
  taskId: number,
  patch: Partial<PriceUpdatePrintItem>
): PriceUpdateRunState {
  return {
    ...state,
    prints: state.prints.map((item) => (item.taskId === taskId ? { ...item, ...patch } : item)),
  };
}

async function processPrintItem(
  state: PriceUpdateRunState,
  taskId: number,
  dependencies: PriceUpdateRunDependencies,
  onChange: (next: PriceUpdateRunState) => void
): Promise<PriceUpdateRunState> {
  const target = state.prints.find((item) => item.taskId === taskId);
  if (!target) {
    return state;
  }
  let current = replacePrintItem(state, taskId, { status: "printing", error: undefined });
  onChange(current);
  let printed = target.printedAwaitingConfirm;
  try {
    if (!printed) {
      await dependencies.printLabel(target.task);
      printed = true;
      current = replacePrintItem(current, taskId, { printedAwaitingConfirm: true });
    }
    await dependencies.markPrinted(taskId);
    current = replacePrintItem(current, taskId, { status: "printed", printedAwaitingConfirm: false });
  } catch (error) {
    current = replacePrintItem(current, taskId, {
      status: "failed",
      printedAwaitingConfirm: printed,
      error: describeError(error),
    });
  }
  onChange(current);
  return current;
}

export interface RunPriceUpdateBatchOptions {
  tasks: readonly StorePriceUpdateTask[];
  mode: PriceUpdateRunMode;
  /** 标签机是否已连接；未连接时只执行改价。 */
  printerReady: boolean;
  dependencies: PriceUpdateRunDependencies;
  onChange: (next: PriceUpdateRunState) => void;
  shouldStop?: () => boolean;
}

/**
 * 「仅更新 / 更新并打印」主流程：先一次性改价，再串行逐张打印并逐张回写。
 * 打印失败的任务不回写，后端仍是「待换标签」留在未完成里。
 */
export async function runPriceUpdateBatch({
  tasks,
  mode,
  printerReady,
  dependencies,
  onChange,
  shouldStop,
}: RunPriceUpdateBatchOptions): Promise<PriceUpdateRunState> {
  const applyItems = buildApplyItems(tasks);
  let state: PriceUpdateRunState = {
    mode,
    phase: "applying",
    apply: { total: applyItems.length, success: 0, failed: 0, targetChanged: 0 },
    hqSyncEnabled: false,
    hqSyncSubmittedCount: 0,
    prints: [],
    printerUnavailable: mode === "applyAndPrint" && !printerReady,
    stopped: false,
  };
  onChange(state);

  let applyResult: PriceUpdateBatchResult | null = null;
  if (applyItems.length > 0) {
    try {
      applyResult = await dependencies.apply(applyItems);
      state = {
        ...state,
        apply: summarizeApplyResult(applyItems.length, applyResult),
        hqSyncEnabled: applyResult.hqSyncEnabled,
        hqSyncSubmittedCount: applyResult.hqSyncSubmittedCount,
      };
    } catch (error) {
      state = {
        ...state,
        apply: { ...summarizeApplyResult(applyItems.length, null), error: describeError(error) },
      };
    }
  }

  if (mode === "applyOnly" || !printerReady) {
    state = { ...state, phase: "done" };
    onChange(state);
    return state;
  }

  state = { ...state, phase: "printing", prints: resolvePrintQueue(tasks, applyResult) };
  onChange(state);

  if (state.prints.length > 0) {
    try {
      state = await dependencies.runPrintExclusive(async () => {
        let current = state;
        for (const item of state.prints) {
          if (shouldStop?.()) {
            current = {
              ...current,
              stopped: true,
              prints: current.prints.map((entry) =>
                entry.status === "pending" ? { ...entry, status: "skipped" as const } : entry
              ),
            };
            onChange(current);
            break;
          }
          current = await processPrintItem(current, item.taskId, dependencies, onChange);
        }
        return current;
      });
    } catch (error) {
      // 互斥被占用等整体失败：全部标记失败，任务仍留在未完成可稍后重试。
      const message = describeError(error);
      state = {
        ...state,
        prints: state.prints.map((entry) =>
          entry.status === "pending" || entry.status === "printing"
            ? { ...entry, status: "failed" as const, error: message }
            : entry
        ),
      };
    }
  }

  state = { ...state, phase: "done" };
  onChange(state);
  return state;
}

/** 进度面板里对单条失败/已停止的标签重试。 */
export async function retryPriceUpdatePrintItem(
  state: PriceUpdateRunState,
  taskId: number,
  dependencies: PriceUpdateRunDependencies,
  onChange: (next: PriceUpdateRunState) => void
): Promise<PriceUpdateRunState> {
  const target = state.prints.find((item) => item.taskId === taskId);
  if (!target || (target.status !== "failed" && target.status !== "skipped")) {
    return state;
  }
  try {
    return await dependencies.runPrintExclusive(() =>
      processPrintItem(state, taskId, dependencies, onChange)
    );
  } catch (error) {
    const next = replacePrintItem(state, taskId, { status: "failed", error: describeError(error) });
    onChange(next);
    return next;
  }
}

export interface PriceUpdatePrintProgress {
  total: number;
  /** 已处理（成功 + 失败 + 跳过）张数，用于进度条。 */
  processed: number;
  printed: number;
  failed: number;
}

export function summarizePrintProgress(state: PriceUpdateRunState): PriceUpdatePrintProgress {
  const printed = state.prints.filter((item) => item.status === "printed").length;
  const failed = state.prints.filter((item) => item.status === "failed" || item.status === "skipped").length;
  return { total: state.prints.length, processed: printed + failed, printed, failed };
}
