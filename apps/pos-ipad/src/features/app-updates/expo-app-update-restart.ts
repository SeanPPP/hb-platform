import type {
  AppUpdateRestartPort,
  AppUpdateRestartSafetySnapshot,
} from "./app-update-coordinator";

export interface ExpoUpdatesReloadPort {
  checkForUpdateAsync(): Promise<Readonly<{ isAvailable: boolean }>>;
  fetchUpdateAsync(): Promise<Readonly<{ isNew: boolean }>>;
  reloadAsync(): Promise<void>;
}

export type ExpoAppUpdateRestartOptions = Readonly<{
  getSafetySnapshot():
    | AppUpdateRestartSafetySnapshot
    | Promise<AppUpdateRestartSafetySnapshot>;
  updates: ExpoUpdatesReloadPort;
}>;

/**
 * 只应用与当前 runtimeVersion 兼容且确实下载到的新 EAS bundle。
 * 原生依赖、Expo SDK 或 runtimeVersion 变化仍必须发布新二进制。
 */
export class ExpoAppUpdateRestartPort implements AppUpdateRestartPort {
  public constructor(
    private readonly options: ExpoAppUpdateRestartOptions,
  ) {}

  public readonly getSafetySnapshot = () =>
    this.options.getSafetySnapshot();

  public async restart(): Promise<void> {
    const update = await this.options.updates.checkForUpdateAsync();
    if (update.isAvailable !== true) return;
    const fetched = await this.options.updates.fetchUpdateAsync();
    if (fetched.isNew !== true) return;
    await this.options.updates.reloadAsync();
  }
}
