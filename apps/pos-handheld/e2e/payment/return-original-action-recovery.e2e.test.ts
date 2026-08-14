import assert from "node:assert/strict";
import test from "node:test";

import {
  DurableReturnExecutionOrchestrator,
  type CompleteDurableReturnAction,
  type DurableReturnAction,
  type DurableReturnAllocation,
  type PrepareDurableReturnAction,
  type ReturnExecutionLedgerPort,
} from "@/features/returns/adapters/durable-return-execution-orchestrator";
import type { ReturnExecutionCommand } from "@/features/returns/return-workflow";

test("return fault matrix: Unknown original online refund resumes the same opaque allocation without a second submit", async () => {
  const ledger = new MemoryReturnLedger();
  const online = new OriginalProviderRefundPort();
  const orchestrator = new DurableReturnExecutionOrchestrator({
    ledger,
    trustedIdentity: {
      async getTrustedIdentity() {
        return {
          storeCode: "S1",
          deviceCode: "IPAD1",
          cashierId: "cashier-1",
          cashierName: "Cashier",
          sessionEpoch: "trusted-epoch-1",
        };
      },
    },
    cashRefund: {
      async submit() { throw new Error("cash must not be selected"); },
      async recover() { throw new Error("cash must not be selected"); },
    },
    onlineRefund: online,
    fingerprint: { async digest() { return "return-fingerprint-1"; } },
    lineMaterial: {
      async resolveForAction() {
        return [{
          lineId: "return-line-1",
          selectionKey: "selection-1",
          sourceKind: "receipt" as const,
          returnSourceKey: "source-1",
          originalOrderGuid: "original-order-1",
          originalOrderDetailGuid: "original-detail-1",
          productCode: "P100",
          itemNumber: "ITEM100",
          lookupCode: "9320001",
          displayName: "Refunded item",
          quantity: 1,
          unitRefundCents: 500,
          availableQuantity: 1,
          remainingAmountCents: 500,
          signedAmountCents: -500,
          syncProvenance: {
            referenceCode: "ORIGINAL-REF-1",
            priceSource: 0,
          },
        }];
      },
    },
    createOpaqueId: (() => {
      let next = 0;
      return (kind) => `${kind}-${++next}`;
    })(),
    nowIso: () => "2026-07-28T00:00:00.000Z",
  });
  const command: ReturnExecutionCommand = {
    actionId: "return-action-1",
    noReceiptAuthorizationKey: null,
    plan: {
      sourceKind: "receipt",
      totalRefundCents: 500,
      online: true,
      lines: [{
        sourceKind: "receipt",
        returnSourceKey: "source-1",
        originalOrderGuid: "original-order-1",
        originalOrderDetailGuid: "original-detail-1",
        productCode: "P100",
        quantity: 1,
        signedAmountCents: -500,
        syncProvenance: {
          referenceCode: "ORIGINAL-REF-1",
          priceSource: 0,
        },
      }],
      allocations: [{
        method: "card",
        signedAmountCents: -500,
        originalCapacityId: "opaque-original-card-capacity",
        originalOrderGuid: "original-order-1",
        offlineCashProof: null,
      }],
    },
  };

  const unknown = await orchestrator.execute(command);
  assert.equal(unknown.status, "unknown");
  assert.ok(unknown.recoveryKey);
  assert.equal(online.submitInputs.length, 1);
  assert.equal(online.submitInputs[0]?.method, "card");
  assert.equal(online.submitInputs[0]?.capacityId, "opaque-original-card-capacity");
  assert.equal(JSON.stringify(online.submitInputs[0]).includes("payment-secret"), false);

  const completed = await orchestrator.recover({
    actionId: command.actionId,
    recoveryKey: unknown.recoveryKey,
  });
  assert.deepEqual(completed, { status: "completed", returnOrderGuid: "return-order-1" });
  assert.equal(online.submitInputs.length, 1);
  assert.equal(online.recoverInputs.length, 1);
  assert.equal(online.recoverInputs[0]?.externalAttemptId, online.submitInputs[0]?.externalAttemptId);
  assert.equal(ledger.completed.length, 1);
  assert.equal(ledger.completed[0]?.returnOrderGuid, "return-order-1");
});

class OriginalProviderRefundPort {
  public readonly submitInputs: Readonly<Record<string, unknown>>[] = [];
  public readonly recoverInputs: Readonly<Record<string, unknown>>[] = [];
  public async prepareAttempt() {
    return {
      attemptKind: "payment-provider" as const,
      externalActionId: "provider-action-original",
      durableAttemptId: "payment-attempt-original",
    };
  }
  public async submit(input: Readonly<Record<string, unknown>>) {
    this.submitInputs.push(input);
    return { status: "unknown" as const, protectedRecoveryKey: "protected-provider-recovery" };
  }
  public async recover(input: Readonly<Record<string, unknown>>) {
    this.recoverInputs.push(input);
    return { status: "completed" as const };
  }
}

class MemoryReturnLedger implements ReturnExecutionLedgerPort {
  private action: DurableReturnAction | null = null;
  public readonly completed: CompleteDurableReturnAction[] = [];
  public async prepareOrLoad(draft: PrepareDurableReturnAction): Promise<DurableReturnAction> {
    if (!this.action) this.action = { ...draft, status: "processing", completedAtIso: null };
    return this.require();
  }
  public async load(actionId: string): Promise<DurableReturnAction | null> {
    return this.action?.actionId === actionId ? this.require() : null;
  }
  public async markAllocationSubmitted(input: Readonly<{ actionId: string; allocationId: string }>): Promise<boolean> {
    const action = this.require();
    if (action.actionId !== input.actionId) return false;
    return this.updateAllocation(input.allocationId, (allocation) =>
      allocation.status === "created" ? { ...allocation, status: "submitted" } : null,
    );
  }
  public async bindAllocationAttempt(input: Readonly<{
    actionId: string;
    allocationId: string;
    attemptKind: "payment-provider" | "hbpos-api";
    externalActionId: string;
    durableAttemptId: string;
  }>): Promise<boolean> {
    const action = this.require();
    if (action.actionId !== input.actionId) return false;
    return this.updateAllocation(input.allocationId, (allocation) =>
      allocation.externalActionId === null && allocation.durableAttemptId === null
        ? {
            ...allocation,
            externalAttemptKind: input.attemptKind,
            externalActionId: input.externalActionId,
            durableAttemptId: input.durableAttemptId,
          }
        : null,
    );
  }
  public async recordAllocationOutcome(input: Readonly<{
    actionId: string;
    allocationId: string;
    expectedStatuses: readonly ("submitted" | "unknown")[];
    status: "completed" | "declined" | "unknown";
    protectedRecoveryKey: string | null;
  }>): Promise<boolean> {
    const action = this.require();
    if (action.actionId !== input.actionId) return false;
    return this.updateAllocation(input.allocationId, (allocation) =>
      input.expectedStatuses.includes(allocation.status as "submitted" | "unknown")
        ? { ...allocation, status: input.status, protectedRecoveryKey: input.protectedRecoveryKey }
        : null,
    );
  }
  public async markActionUnknown(input: Readonly<{ actionId: string }>): Promise<void> {
    if (this.require().actionId === input.actionId) this.action = { ...this.require(), status: "unknown" };
  }
  public async resumeUnknownAction(input: Readonly<{ actionId: string }>): Promise<boolean> {
    const action = this.require();
    if (action.actionId !== input.actionId || action.status !== "unknown") return false;
    this.action = { ...action, status: "processing" };
    return true;
  }
  public async markActionDeclined(input: Readonly<{ actionId: string }>): Promise<void> {
    if (this.require().actionId === input.actionId) this.action = { ...this.require(), status: "declined" };
  }
  public async completeAtomically(input: CompleteDurableReturnAction): Promise<DurableReturnAction> {
    const action = this.require();
    assert.equal(action.actionId, input.actionId);
    this.completed.push(input);
    this.action = { ...action, status: "completed", completedAtIso: input.completedAtIso };
    return this.require();
  }
  private updateAllocation(
    allocationId: string,
    update: (allocation: DurableReturnAllocation) => DurableReturnAllocation | null,
  ): boolean {
    const action = this.require();
    let changed = false;
    const allocations = action.allocations.map((allocation) => {
      if (allocation.allocationId !== allocationId) return allocation;
      const next = update(allocation);
      if (!next) return allocation;
      changed = true;
      return next;
    });
    if (changed) this.action = { ...action, allocations };
    return changed;
  }
  private require(): DurableReturnAction {
    assert.ok(this.action, "return action must be prepared before mutation");
    return this.action;
  }
}
