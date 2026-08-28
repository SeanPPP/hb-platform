export type LocalOrderState =
  | "Draft"
  | "Completing"
  | "CompletedLocal"
  | "PendingSync"
  | "Syncing"
  | "Synced"
  | "Blocked403"
  | "Rejected";

export type PaymentAttemptState =
  | "Created"
  | "Submitted"
  | "Pending"
  | "Approved"
  | "Declined"
  | "Cancelled"
  | "Unknown";

export type PrintJobState = "Queued" | "Sending" | "Printed" | "Failed" | "Ambiguous";

export type DrawerEventState = "Required" | "Requested" | "Completed" | "Failed" | "Unknown";

const orderTransitions: Readonly<Record<LocalOrderState, readonly LocalOrderState[]>> = {
  Draft: ["Completing"],
  Completing: ["CompletedLocal", "PendingSync"],
  CompletedLocal: ["PendingSync"],
  PendingSync: ["Syncing"],
  Syncing: ["Synced", "PendingSync", "Blocked403", "Rejected"],
  Synced: [],
  Blocked403: ["PendingSync"],
  Rejected: [],
};

const paymentTransitions: Readonly<Record<PaymentAttemptState, readonly PaymentAttemptState[]>> = {
  Created: ["Submitted", "Cancelled"],
  Submitted: ["Pending", "Approved", "Declined", "Cancelled", "Unknown"],
  Pending: ["Approved", "Declined", "Cancelled", "Unknown"],
  Approved: [],
  Declined: [],
  Cancelled: [],
  Unknown: ["Pending", "Approved", "Declined", "Cancelled"],
};

export function canTransitionOrder(from: LocalOrderState, to: LocalOrderState): boolean {
  return orderTransitions[from].includes(to);
}

export function canTransitionPaymentAttempt(
  from: PaymentAttemptState,
  to: PaymentAttemptState,
): boolean {
  return paymentTransitions[from].includes(to);
}
