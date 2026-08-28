/**
 * 共享业务只依赖普通操作租约；更新切换的状态机与互斥策略仍由各 App 持有。
 */
export type UpdateOperationLeasePort = Readonly<{
  runOperation<T>(operation: () => T | Promise<T>): Promise<T>;
}>;
