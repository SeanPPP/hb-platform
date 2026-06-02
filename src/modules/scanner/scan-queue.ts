import type { ScanSource } from "@/modules/scanner/types";

export interface ScanQueueJob {
  barcode: string;
  scanTraceId: string;
  receivedAt?: number;
  source: ScanSource;
  storeCode?: string | null;
}

interface RecentScanEntry {
  barcodeKey: string;
  acceptedAt: number;
}

export interface ScanQueueState {
  active: ScanQueueJob | null;
  pending: ScanQueueJob[];
  recentAccepted: RecentScanEntry[];
}

export interface ScanQueueOptions {
  duplicateWindowMs: number;
  maxSize: number;
}

export type EnqueueScanDecision =
  | { type: "queued"; queueSize: number }
  | { type: "duplicate"; queueSize: number; reason: "recent-barcode" }
  | { type: "overflow"; queueSize: number; droppedJob: ScanQueueJob | null };

export type StartScanDecision =
  | { type: "started"; queueSize: number }
  | { type: "duplicate"; queueSize: number; reason: "recent-barcode" };

export function createInitialScanQueue(): ScanQueueState {
  return {
    active: null,
    pending: [],
    recentAccepted: [],
  };
}

function getBarcodeKey(barcode: string) {
  return barcode.trim();
}

function pruneRecentAccepted(queue: ScanQueueState, now: number, duplicateWindowMs: number) {
  const windowStartedAt = now - duplicateWindowMs;
  return queue.recentAccepted.filter((entry) => entry.acceptedAt >= windowStartedAt);
}

export function startScanJob(
  queue: ScanQueueState,
  job: ScanQueueJob,
  now: number,
  options: ScanQueueOptions
): { queue: ScanQueueState; decision: StartScanDecision } {
  const barcodeKey = getBarcodeKey(job.barcode);
  const recentAccepted = pruneRecentAccepted(queue, now, options.duplicateWindowMs);

  if (recentAccepted.some((entry) => entry.barcodeKey === barcodeKey)) {
    return {
      queue: {
        ...queue,
        recentAccepted,
      },
      decision: {
        type: "duplicate",
        queueSize: queue.pending.length,
        reason: "recent-barcode",
      },
    };
  }

  return {
    queue: {
      active: job,
      pending: queue.pending,
      recentAccepted: [
        ...recentAccepted,
        {
          barcodeKey,
          acceptedAt: now,
        },
      ],
    },
    decision: {
      type: "started",
      queueSize: queue.pending.length,
    },
  };
}

export function enqueueScanJob(
  queue: ScanQueueState,
  job: ScanQueueJob,
  now: number,
  options: ScanQueueOptions
): { queue: ScanQueueState; decision: EnqueueScanDecision } {
  const barcodeKey = getBarcodeKey(job.barcode);
  const recentAccepted = pruneRecentAccepted(queue, now, options.duplicateWindowMs);

  if (recentAccepted.some((entry) => entry.barcodeKey === barcodeKey)) {
    return {
      queue: {
        ...queue,
        recentAccepted,
      },
      decision: {
        type: "duplicate",
        queueSize: queue.pending.length,
        reason: "recent-barcode",
      },
    };
  }

  const maxSize = Math.max(0, options.maxSize);
  const pendingWithJob = [...queue.pending, job];
  const droppedJob = pendingWithJob.length > maxSize ? pendingWithJob[0] ?? null : null;
  const pending = pendingWithJob.slice(Math.max(0, pendingWithJob.length - maxSize));
  const nextQueue = {
    ...queue,
    pending,
    recentAccepted: [
      ...recentAccepted,
      {
        barcodeKey,
        acceptedAt: now,
      },
    ],
  };

  if (droppedJob) {
    return {
      queue: nextQueue,
      decision: {
        type: "overflow",
        queueSize: pending.length,
        droppedJob,
      },
    };
  }

  return {
    queue: nextQueue,
    decision: {
      type: "queued",
      queueSize: pending.length,
    },
  };
}

export function completeActiveScanJob(
  queue: ScanQueueState,
  expectedScanTraceId?: string
): { queue: ScanQueueState; nextJob: ScanQueueJob | null; completed: boolean } {
  if (expectedScanTraceId && queue.active?.scanTraceId !== expectedScanTraceId) {
    return {
      queue,
      nextJob: null,
      completed: false,
    };
  }

  const [nextJob = null, ...pending] = queue.pending;

  return {
    queue: {
      ...queue,
      active: nextJob,
      pending,
    },
    nextJob,
    completed: true,
  };
}

export function canCompleteScanJob(
  queue: ScanQueueState,
  job: ScanQueueJob,
  jobGeneration: number,
  currentGeneration: number
) {
  return jobGeneration === currentGeneration && queue.active?.scanTraceId === job.scanTraceId;
}
