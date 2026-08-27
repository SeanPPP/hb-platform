export type DeviceRegistrationMutationLease = Readonly<{
  release(): void;
}>;

/** API 地址切换与开通/重置写操作共享的同步门闩。 */
export class DeviceRegistrationApiPartitionGuard {
  private activeMutations = 0;
  private switchActive = false;

  public beginMutation(): DeviceRegistrationMutationLease {
    if (this.switchActive) {
      throw new Error("DEVICE_REGISTRATION_API_PARTITION_SWITCH_ACTIVE");
    }
    this.activeMutations += 1;
    let released = false;
    return Object.freeze({
      release: () => {
        if (released) return;
        released = true;
        this.activeMutations -= 1;
      },
    });
  }

  public async runSwitch<T>(operation: () => Promise<T>): Promise<
    | Readonly<{ blocked: true }>
    | Readonly<{ blocked: false; value: T }>
  > {
    if (this.switchActive || this.activeMutations > 0) {
      return Object.freeze({ blocked: true });
    }
    this.switchActive = true;
    try {
      return Object.freeze({ blocked: false, value: await operation() });
    } finally {
      this.switchActive = false;
    }
  }
}
