import type { LocalHistoryPresenter } from "./local-history-presenter";

export interface LocalHistoryPresenterFactory {
  createPresenter(): LocalHistoryPresenter;
}

export function resolveLocalHistoryPresenterFactory(
  services: unknown,
): LocalHistoryPresenterFactory | null {
  if (!isRecord(services)) return null;
  const candidate = services.localHistory;
  if (
    !isRecord(candidate) ||
    typeof candidate.createPresenter !== "function"
  ) {
    return null;
  }
  return candidate as unknown as LocalHistoryPresenterFactory;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
