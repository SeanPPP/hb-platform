export type DrawerResult = Readonly<{
  status: "completed" | "failed" | "unknown";
  errorCode: string | null;
}>;

export interface CashDrawerPort {
  open(eventId: string): Promise<DrawerResult>;
}
