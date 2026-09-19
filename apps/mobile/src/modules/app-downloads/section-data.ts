import { appDownloadsApi } from "./api";
import { mergeHandheldPolicyCandidates } from "./logic";
import type {
  AppDownloadEnvironment,
  AppDownloadPlatform,
  AppDownloadsSection,
  HandheldCandidate,
  HandheldPolicy,
  IpadOtaRelease,
  IpadOtaRollout,
  NativePolicy,
  NativeRelease,
  OtaPolicy,
  OtaRelease,
  Revision,
  StoreOption,
} from "./types";

export type SectionData =
  | {
      kind: "native";
      app: "mobile-ios" | "pos-ipad";
      releases: NativeRelease[];
      policy: NativePolicy;
      stores: StoreOption[];
    }
  | {
      kind: "mobile-ota";
      releases: OtaRelease[];
      policy: OtaPolicy;
      revisions: Revision[];
    }
  | {
      kind: "ipad-ota";
      releases: IpadOtaRelease[];
      rollout: IpadOtaRollout;
      stores: StoreOption[];
    }
  | {
      kind: "handheld";
      policies: HandheldPolicy[];
      candidates: HandheldCandidate[];
      revisions: Revision[];
    };

const HANDHELD_LANES = [
  "android-native",
  "ios-native",
  "android-ota",
  "ios-ota",
] as const;

/** 只有 Mobile OTA 依赖环境和平台；其余分区的查询键不带它们，切换环境时不重复请求。 */
export function sectionQueryKey(
  section: AppDownloadsSection,
  profile: AppDownloadEnvironment,
  platform: AppDownloadPlatform,
) {
  return section === "mobile-ota"
    ? (["app-downloads-section", section, profile, platform] as const)
    : (["app-downloads-section", section] as const);
}

export async function loadSectionData(
  section: AppDownloadsSection,
  profile: AppDownloadEnvironment,
  platform: AppDownloadPlatform,
): Promise<SectionData> {
  if (section === "mobile-native" || section === "ipad-native") {
    const app = section === "mobile-native" ? "mobile-ios" : "pos-ipad";
    const [releases, policy, stores] = await Promise.all([
      appDownloadsApi.getIosReleases(app),
      appDownloadsApi.getNativePolicy(app),
      section === "ipad-native"
        ? appDownloadsApi.getStoreOptions()
        : Promise.resolve([]),
    ]);
    return { kind: "native", app, releases, policy, stores };
  }
  if (section === "mobile-ota") {
    const [releases, policy, revisions] = await Promise.all([
      appDownloadsApi.getMobileOtaReleases(profile, platform),
      appDownloadsApi.getMobileOtaPolicy(profile, platform),
      appDownloadsApi.getMobileOtaRevisions(profile, platform),
    ]);
    return { kind: "mobile-ota", releases, policy, revisions };
  }
  if (section === "ipad-ota") {
    const [releases, rollout, stores] = await Promise.all([
      appDownloadsApi.getIpadOtaReleases(),
      appDownloadsApi.getIpadOtaRollout(),
      appDownloadsApi.getStoreOptions(),
    ]);
    return { kind: "ipad-ota", releases, rollout, stores };
  }
  const [policies, candidates, revisions] = await Promise.all([
    appDownloadsApi.getHandheldPolicies(),
    Promise.all(
      HANDHELD_LANES.map((lane) =>
        appDownloadsApi.getHandheldCandidates(
          lane.startsWith("ios") ? "ios" : "android",
          lane.endsWith("ota") ? "ota" : "native",
        ),
      ),
    ).then((values) => values.flat()),
    Promise.all(
      HANDHELD_LANES.map((lane) =>
        appDownloadsApi
          .getHandheldRevisions(lane)
          // 按请求的通道补齐 lane，界面按通道筛选修订记录
          .then((rows) =>
            rows.map((row) => ({ ...row, lane: row.lane ?? lane })),
          ),
      ),
    ).then((values) => values.flat()),
  ]);
  return {
    kind: "handheld",
    policies,
    candidates: mergeHandheldPolicyCandidates(candidates, policies),
    revisions,
  };
}
