# Task 2A 实施报告

## 结果

- 历史恢复状态不再传播到新订单付款页；当前 in-flight / ResultUnknown 在安全移交成功前继续锁定本单。
- 只有唯一匹配的持久化 attempt 与完整 `CardPaymentOrderDraft` 才显示并启用“移至异常中心并开始新单”。确认时会再次按 provider、AttemptGuid、会话、购物车和 tender 核验。
- 查询失败、缺失/错配 attempt、空坏 draft、候选失效均保持本单与锁，不清购物车、不导航。
- 确认移交只清当前付款 UI、购物车和当前锁，并明确回到 POS；持久化恢复数据仅只读核验，未删除或修改。
- 已提供 `OpenCardRecoveryCenterCommand` / `OpenCardRecoveryCenter` action 契约，未实现 Task 3 页面。

## 红绿证据

- 红：`dotnet test ... --filter FullyQualifiedName~Payment_page_prepare_for_entry_drops_historical_recovery_lock_for_new_order --artifacts-path .artifacts/task2a-red1`：1 失败。
- 绿：同测试使用 `.artifacts/task2a-green1`：1 通过。
- 红：无资格移交按钮测试 `.artifacts/task2a-red2`：1 失败；绿 `.artifacts/task2a-green2`：1 通过。
- 红：overlay 后命令立即启用 `.artifacts/task2a-red3b`：1 失败；绿 `.artifacts/task2a-green3`：1 通过。
- 红：确认移交清单/导航 `.artifacts/task2a-red4`：1 失败；绿并入 `.artifacts/task2a-green5`：3 通过。
- 红：查询异常与 stale candidate `.artifacts/task2a-red5`：2 失败；绿 `.artifacts/task2a-green5`：3 通过。
- 红：资格判定 API `.artifacts/task2a-red6`：编译失败；绿 `.artifacts/task2a-green6f`：6 通过。
- 红：异常中心导航契约 `.artifacts/task2a-red7`：编译失败；绿 `.artifacts/task2a-green7`：1 通过。
- 最终聚焦：`.artifacts/task2a-final-focused`：15/15 通过。
- 付款页整类：`.artifacts/task2a-payment-class-2`：174/176 通过；2 项失败位于共享工作者修改中的 `CashPaymentWorkflowService.cs`（离线 voucher、installment repayment），不在 Task 2A 归属范围。

## 构建与差异检查

- `dotnet build apps/pos-wpf/src/Hbpos.Client.Wpf/Hbpos.Client.Wpf.csproj --artifacts-path .artifacts/task2a-final-build`：成功，0 warning / 0 error。
- `git diff --check`：通过。
- GitNexus `detect_changes(scope=all)`：low risk，未识别受影响 execution flow；索引对新增文件识别有限，另以显式路径 diff/stage 核对。
