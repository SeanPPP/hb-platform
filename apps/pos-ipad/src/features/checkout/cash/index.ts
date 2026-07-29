// 旧 CashCheckoutService/AtomicCashDatabaseAdapter 仅保留给迁移期单元测试，
// 不从生产入口导出，避免误接入不具备跨重启幂等能力的实现。
export type {
  CashCheckoutDependencies,
  CashDrawerDisposition,
  CashCheckoutInput,
  CashCheckoutResult,
  LocalSequencePort,
} from "./cash-checkout-service";
export * from "./durable-cash-checkout-service";
