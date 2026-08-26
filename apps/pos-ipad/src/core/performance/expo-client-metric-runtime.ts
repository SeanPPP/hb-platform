import { ExpoSqliteDriver } from "../db/expo-sqlite-driver";
import type { ApplicationLogCenterConfig } from "../logging/application-log";

import {
  ClientMetricRecorder,
  ClientMetricRuntime,
  ClientMetricSampler,
  ClientMetricSamplingPolicyState,
  ClientMetricUploader,
  SqliteClientMetricOutbox,
  SqliteClientMetricSamplingPolicyStore,
  initializeClientMetricOutbox,
  resolveClientMetricUploadConfig,
} from "./client-metrics";

const METRIC_DATABASE_NAME = "hb-pos-ipad-performance.db";

export async function createExpoClientMetricRuntime(input: Readonly<{
  appVersion: string;
  channel: string;
  store: string | null;
  sessionId: string;
  logCenter: ApplicationLogCenterConfig;
  createId(): string;
  getRequestHeaders(): Promise<Readonly<Record<string, string>> | null>;
}>): Promise<ClientMetricRuntime> {
  const database = await new ExpoSqliteDriver().open(METRIC_DATABASE_NAME);
  try {
    await initializeClientMetricOutbox(database);
    const nowIso = () => new Date().toISOString();
    const outbox = new SqliteClientMetricOutbox(database, nowIso);
    const policyStore = new SqliteClientMetricSamplingPolicyStore(
      database,
      nowIso,
    );
    const policyState = new ClientMetricSamplingPolicyState(
      await policyStore.read(),
    );
    return new ClientMetricRuntime(
      new ClientMetricRecorder({
        outbox,
        sampler: new ClientMetricSampler({
          policyState,
          sessionId: input.sessionId,
        }),
        context: {
          app: "pos-ipad",
          version: input.appVersion,
          channel: input.channel,
          store: input.store,
          environment: input.logCenter.environment,
        },
        createId: input.createId,
        nowIso,
      }),
      new ClientMetricUploader({
        outbox,
        config: resolveClientMetricUploadConfig({
          enabled: input.logCenter.enabled,
          logIngestUrl: input.logCenter.ingestUrl,
          writeKey: input.logCenter.writeKey,
          projectCode: "hbpos_ipad",
          environment: input.logCenter.environment,
        }),
        samplingPolicy: { state: policyState, store: policyStore },
        getRequestHeaders: input.getRequestHeaders,
      }),
      () => database.close(),
    );
  } catch (error) {
    await database.close().catch(() => undefined);
    throw error;
  }
}
