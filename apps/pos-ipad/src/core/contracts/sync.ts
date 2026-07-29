export type SyncFailureKind =
  | "network"
  | "server"
  | "unauthorized"
  | "forbidden"
  | "business-rejection";

export type SyncOrderResult =
  | Readonly<{ kind: "synced"; alreadySynced: boolean }>
  | Readonly<{ kind: "retry"; failure: Extract<SyncFailureKind, "network" | "server" | "unauthorized"> }>
  | Readonly<{ kind: "blocked"; failure: "forbidden"; code: string }>
  | Readonly<{ kind: "rejected"; failure: "business-rejection"; code: string }>;

export interface OrderSyncPort {
  sync(orderGuid: string, payloadJson: string): Promise<SyncOrderResult>;
}
