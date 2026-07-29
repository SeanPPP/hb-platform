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
    );
  }

  public onForeground(): Promise<void> {
    return this.runWithHardware(
      () => this.services.sync.onForeground(),
      () => this.services.appUpdates?.refreshOnForeground(),
    );
  }

  public async onNetworkChanged(isOnline: boolean): Promise<void> {
    await this.services.sync.onNetworkChanged(isOnline);
    if (isOnline) {
      await this.services.appUpdates?.refreshOnNetworkAvailable();
    }
  }

  private async runWithHardware(
    sync: () => Promise<unknown>,
    refreshAppUpdate: () => Promise<unknown> | undefined,
  ): Promise<void> {
    await Promise.all([
      sync(),
      this.drainHardware(),
      refreshAppUpdate(),
    ]);
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
}
