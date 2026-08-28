/**
 * 共享业务只依赖普通操作租约；更新切换的状态机与互斥策略仍由各 App 持有。
 */
export type UpdateOperationLeasePort = Readonly<{
  runOperation<T>(operation: () => T | Promise<T>): Promise<T>;
  /**
   * 更新切换完全释放后触发；定时同步依赖它恢复被 transition 拒绝的 wake。
   */
  subscribeTransitionReleased(listener: () => void): () => void;
}>;
