/**
 * 网络恢复的 React 绑定：
 * - NetworkRecoveryProvider：挂载到根布局，负责创建控制器、监听 App 前后台切换、
 *   启动退避重试循环，并通过 Context 暴露状态与操作；
 * - useNetworkRecovery：组件内读取恢复状态与 enqueue 能力。
 *
 * 监听策略（零原生依赖）：
 * - AppState（React Native 内建）监听 App 回到前台，立即触发后端探测与补传；
 * - 指数退避定时器兜底：离线期间的入队请求会自动周期性重试，直到后端可达。
 */
import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type PropsWithChildren,
} from "react";
import { AppState } from "react-native";

import { reviewAwareFetch } from "@/modules/ios-review/network";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { checkBackendReachable } from "./health-check";
import {
  NetworkRecoveryController,
  type NetworkRecoveryControllerDeps,
  type NetworkRecoveryState,
} from "./network-recovery-controller";
import {
  OfflineRequestQueue,
  type EnqueueRequestInput,
} from "./offline-queue";

export type NetworkRecoveryContextValue = {
  /** 当前恢复状态（isOnline / isBackendReachable / pendingCount / isFlushing）。 */
  state: NetworkRecoveryState;
  /** 业务请求网络失败时入队，等待恢复后补传。 */
  enqueue: (input: EnqueueRequestInput) => Promise<void>;
  /** 立即触发一次后端探测与补传。 */
  triggerRecovery: () => Promise<void>;
  /** 控制器是否已启用（review 审核态或未就绪时为 false）。 */
  enabled: boolean;
};

const NetworkRecoveryContext = createContext<NetworkRecoveryContextValue | null>(
  null,
);

export type NetworkRecoveryProviderProps = PropsWithChildren<{
  /** 是否启用：false 时不监听、不补传（iOS 审核态或启动未就绪时关闭）。 */
  enabled: boolean;
  /** 依赖注入点，便于测试或复用自定义队列/发送实现。 */
  deps?: Omit<NetworkRecoveryControllerDeps, "queue" | "checkBackend" | "send"> & {
    queue?: OfflineRequestQueue;
    checkBackend?: () => Promise<boolean>;
    send?: NetworkRecoveryControllerDeps["send"];
  };
}>;

/** 生产默认依赖：AsyncStorage 队列 + 通用 health 探测 + reviewAwareFetch 发送。 */
function createDefaultController(
  deps: NetworkRecoveryProviderProps["deps"],
): NetworkRecoveryController {
  return new NetworkRecoveryController({
    queue: deps?.queue ?? new OfflineRequestQueue(),
    checkBackend:
      deps?.checkBackend ??
      (async () => (await checkBackendReachable()).ok),
    send:
      deps?.send ??
      (async (request) => {
        // 统一走 reviewAwareFetch，与考勤/审核态拦截保持一致；
        // 审核态下会抛 IOS_REVIEW_NETWORK_BLOCKED，由补传失败重试逻辑兜底。
        await reviewAwareFetch(request.url, {
          method: request.method,
          headers: request.headers,
          body: request.body,
        });
      }),
    schedule: deps?.schedule,
    nowIso: deps?.nowIso,
    onLog: deps?.onLog,
  });
}

export function NetworkRecoveryProvider({
  enabled,
  deps,
  children,
}: NetworkRecoveryProviderProps) {
  // 控制器在整个生命周期内稳定（enabled 变化不重建，避免丢失退避状态）。
  const controllerRef = useRef<NetworkRecoveryController | null>(null);
  if (controllerRef.current === null) {
    controllerRef.current = createDefaultController(deps);
  }
  const controller = controllerRef.current;

  const [state, setState] = useState<NetworkRecoveryState>(
    controller.getState(),
  );

  // 订阅状态变化（仅一次）。
  useEffect(() => controller.subscribe(setState), [controller]);

  // enabled 开关：启动时（appReady 就绪）start 并处理遗留队列；关闭或卸载时 stop。
  useEffect(() => {
    if (!enabled) {
      controller.stop();
      return undefined;
    }
    void controller.start().catch((error: unknown) => {
      // 启动失败不阻塞界面；遗留队列会由后续前台/退避触发继续处理。
      console.warn("[network-recovery] 启动恢复检查失败", {
        error: error instanceof Error ? error.message : String(error),
      });
    });
    return () => controller.stop();
  }, [controller, enabled]);

  // App 回到前台时立即探测后端并补传（controller 内部以 started 状态防护）。
  useEffect(() => {
    const subscription = AppState.addEventListener("change", (next) => {
      if (next === "active") {
        void controller.notifyAppForeground().catch(() => undefined);
      }
    });
    return () => subscription.remove();
  }, [controller]);

  const value = useMemo<NetworkRecoveryContextValue>(
    () => ({
      state,
      enqueue: async (input) => {
        if (!enabled) {
          // 未启用（如审核态）时不入队，避免队列在审核环境积累。
          return;
        }
        await controller.enqueue(input);
      },
      triggerRecovery: async () => {
        if (enabled) {
          await controller.triggerRecovery();
        }
      },
      enabled,
    }),
    [controller, state, enabled],
  );

  return (
    <NetworkRecoveryContext.Provider value={value}>
      {children}
    </NetworkRecoveryContext.Provider>
  );
}

export function useNetworkRecovery(): NetworkRecoveryContextValue {
  const value = useContext(NetworkRecoveryContext);
  if (value === null) {
    throw new Error(
      "useNetworkRecovery 必须在 NetworkRecoveryProvider 内部使用",
    );
  }
  return value;
}

export { NetworkRecoveryController } from "./network-recovery-controller";
