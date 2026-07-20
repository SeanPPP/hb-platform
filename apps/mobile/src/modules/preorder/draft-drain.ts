export interface DraftDrainSnapshot {
  fingerprint: string;
}

export interface DraftDrainRef {
  current: Promise<boolean> | null;
}

export function isDraftContextCurrent(activeContextKey: string, snapshotContextKey: string) {
  return activeContextKey === snapshotContextKey;
}

/**
 * 将防抖保存、离页保存和提交前保存收敛到同一条串行链，并追赶保存期间产生的新输入。
 */
export function drainLatestDraft<TSnapshot extends DraftDrainSnapshot>(
  inFlightRef: DraftDrainRef,
  readLatestSnapshot: () => TSnapshot | null,
  isSnapshotSaved: (snapshot: TSnapshot) => boolean,
  saveSnapshot: (snapshot: TSnapshot) => Promise<boolean>
) {
  if (inFlightRef.current) return inFlightRef.current;

  const request = (async () => {
    while (true) {
      const snapshot = readLatestSnapshot();
      if (!snapshot) return false;
      if (isSnapshotSaved(snapshot)) return true;
      if (!await saveSnapshot(snapshot)) return false;
    }
  })();
  inFlightRef.current = request;
  void request.then(
    () => {
      if (inFlightRef.current === request) inFlightRef.current = null;
    },
    () => {
      if (inFlightRef.current === request) inFlightRef.current = null;
    }
  );
  return request;
}
