import { normalizePublicRuntimeApiBaseUrl } from "../security/pos-public-runtime-configuration";

export type BootstrapServerDiagnostics = Readonly<{
  currentApiBaseUrl: string;
  test(candidate: string, signal: AbortSignal): Promise<boolean>;
}>;

type BootstrapServerDiagnosticsInput = Readonly<{
  currentApiBaseUrl: string;
  trustedApiOrigins: readonly string[];
  probe(healthUrl: string, signal: AbortSignal): Promise<boolean>;
}>;

export function createBootstrapServerDiagnostics(
  input: BootstrapServerDiagnosticsInput,
): BootstrapServerDiagnostics {
  const trustedOrigins = new Set(input.trustedApiOrigins);
  return Object.freeze({
    currentApiBaseUrl: input.currentApiBaseUrl,
    test: (candidate, signal) => {
      const normalized = normalizePublicRuntimeApiBaseUrl(
        candidate,
        trustedOrigins,
      );
      return input.probe(`${normalized}/api/v1/health`, signal);
    },
  });
}
