export type RuntimeBackgroundWorkPort = Readonly<{
  sync: Readonly<{
    onApplicationStarted(): Promise<unknown>;
    onForeground(): Promise<unknown>;
    onNetworkChanged(isOnline: boolean): Promise<unknown>;
  }>;
  fulfilment: Readonly<{
    drainAutomaticQueue(): Promise<unknown>;
  }>;
  appUpdates?: Readonly<{
    refreshOnStartup(): Promise<unknown>;
    refreshOnForeground(): Promise<unknown>;
    refreshOnNetworkAvailable(): Promise<unknown>;
  }>;
  faceAttendance?: Readonly<{
    refresh(): Promise<void>;
    sync(): Promise<void>;
  }>;
}>;

/**
 * 把系统生命周期转换为耐久队列触发器。
 *
 * sync 自身按 OrderGuid 单飞；这里另外让外设 drain 单飞，避免快速前后台切换把
 * 同一个 Queued/Required 动作排入多个串行扫描。错误继续交给调用方记录或忽略，
 * 状态机和 SQLCipher 队列仍保留真实恢复事实。
 */
export class RuntimeWorkController {
  private hardwareDrain: Promise<void> | null = null;

  public constructor(private readonly services: RuntimeBackgroundWorkPort) {}

  public onApplicationStarted(): Promise<void> {
    return this.runWithHardware(
      () => this.services.sync.onApplicationStarted(),
      () => this.services.appUpdates?.refreshOnStartup(),
      () => this.refreshFaceThenSync(),
    );
  }

  public onForeground(): Promise<void> {
    return this.runWithHardware(
      () => this.services.sync.onForeground(),
      () => this.services.appUpdates?.refreshOnForeground(),
      () => this.refreshFaceThenSync(),
    );
  }

  public async onNetworkChanged(isOnline: boolean): Promise<void> {
    const failures: PromiseSettledResult<unknown>[] = [
      ...(await Promise.allSettled([this.services.sync.onNetworkChanged(isOnline)])),
    ];
    if (isOnline) {
      // roster/app-update 失败不可堵住独立的人脸耐久队列；各域各自报告状态并继续重试。
      failures.push(...(await Promise.allSettled([
        this.services.appUpdates?.refreshOnNetworkAvailable(),
        this.refreshFaceThenSync(),
      ])));
    }
    this.throwFailures(failures);
  }

  private async runWithHardware(
    sync: () => Promise<unknown>,
    refreshAppUpdate: () => Promise<unknown> | undefined,
    faceAttendance: () => Promise<unknown> | undefined,
  ): Promise<void> {
    // 先让同步/审计与外设队列到达稳定点，更新门禁才能读取可信安全快照。
    const failures: PromiseSettledResult<unknown>[] = [
      ...(await Promise.allSettled([
      sync(),
      this.drainHardware(),
      ])),
    ];
    failures.push(...(await Promise.allSettled([refreshAppUpdate(), faceAttendance()])));
    this.throwFailures(failures);
  }

  private drainHardware(): Promise<void> {
    if (this.hardwareDrain) return this.hardwareDrain;

    const drain = this.services.fulfilment
      .drainAutomaticQueue()
      .then(() => undefined)
      .finally(() => {
        if (this.hardwareDrain === drain) {
          this.hardwareDrain = null;
        }
      });
    this.hardwareDrain = drain;
    return drain;
  }

  private async refreshFaceThenSync(): Promise<void> {
    const face = this.services.faceAttendance;
    if (!face) return;
    // roster 刷新失败仍要 drain 旧队列，但失败必须回传 bridge 记录，不能被随后成功的 sync 覆盖。
    const refresh = await Promise.allSettled([face.refresh()]);
    const sync = await Promise.allSettled([face.sync()]);
    this.throwFailures([...refresh, ...sync]);
  }

  private throwFailures(results: readonly PromiseSettledResult<unknown>[]): void {
    const reasons = results.flatMap((result) => result.status === "rejected" ? [result.reason] : []);
    if (reasons.length > 0) throw new AggregateError(reasons, "Runtime background work failed.");
  }
}
