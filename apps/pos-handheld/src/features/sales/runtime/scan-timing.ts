/**
 * 扫码加购主路径轻量计时器（仅用于性能测量，默认关闭）。
 *
 * 开启方式：构建/运行时设置 EXPO_PUBLIC_SALES_SCAN_TIMING=1（.env 或 EAS 环境变量）。
 * 计时结果经 console.log 输出到 logcat / Metro 控制台，格式：
 *   [scan-timing] last-character → hid-submit+... → cart-published+... → sound-playing+...
 * 未开启该环境变量时所有方法为空操作，零行为影响、零开销。
 *
 * 注意：本工具属于临时测量设施；真机验收后应关闭构建开关。
 */
type ScanTimingSession = {
  startedAt: number;
  lastAt: number;
  marks: string[];
};

/** 防御性上限：session 泄漏（例如 promise 永不落定）时丢弃最旧记录，避免内存无限增长。 */
const MAX_ACTIVE_SESSIONS = 32;

/** 开关在模块加载时求值一次；Expo 打包时内联 EXPO_PUBLIC_ 环境变量。 */
function resolveScanTimingEnabled(): boolean {
  return (
    typeof process !== "undefined" &&
    process.env.EXPO_PUBLIC_SALES_SCAN_TIMING === "1"
  );
}

export class ScanTimingCollector {
  private readonly enabled: boolean;
  private readonly sessions = new Map<string, ScanTimingSession>();
  private readonly expectedSoundByCue = new Map<string, string>();
  private lastHidCharacterAt: number | null = null;

  public constructor(
    enabled: boolean = resolveScanTimingEnabled(),
    private readonly now: () => number = () => performance.now(),
    private readonly log: (line: string) => void = (line) =>
      console.log(line),
  ) {
    this.enabled = enabled;
  }

  /** 开始一次测量：t0 与首个标签记录在同一时刻。 */
  public begin(id: string | undefined, label: string): void {
    if (!this.enabled || id === undefined) return;
    const startedAt = this.now();
    this.setSession(id, { startedAt, lastAt: startedAt, marks: [label] });
  }

  /** 记录 HID 最后一个字符；beginHid 会消费它，将 85ms 分帧等待计入总耗时。 */
  public noteHidCharacter(): void {
    if (!this.enabled) return;
    this.lastHidCharacterAt = this.now();
  }

  /** 从最近 HID 字符开始一次测量，并立即记录业务收到完整条码的时刻。 */
  public beginHid(id: string | undefined): void {
    if (!this.enabled || id === undefined) return;
    const submittedAt = this.now();
    const startedAt = this.lastHidCharacterAt ?? submittedAt;
    this.lastHidCharacterAt = null;
    this.setSession(id, {
      startedAt,
      lastAt: submittedAt,
      marks: [
        "last-character",
        `hid-submit+${(submittedAt - startedAt).toFixed(1)}ms` +
          `(d+${(submittedAt - startedAt).toFixed(1)}ms)`,
      ],
    });
  }

  /** 将一次业务反馈与下一次对应 cue 的原生播放状态关联，不改变 play(cue) 接口。 */
  public expectSound(
    id: string | undefined,
    cue: string,
  ): void {
    if (!this.enabled || id === undefined || !this.sessions.has(id)) return;
    const supersededId = this.expectedSoundByCue.get(cue);
    if (supersededId && supersededId !== id) {
      this.deleteSession(supersededId);
    }
    this.expectedSoundByCue.set(cue, id);
  }

  /** 原生播放器报告 playing 后结束对应时间线；没有测量会话时为空操作。 */
  public soundPlaying(cue: string): void {
    if (!this.enabled) return;
    const id = this.expectedSoundByCue.get(cue);
    if (!id) return;
    this.expectedSoundByCue.delete(cue);
    this.mark(id, "sound-playing");
    this.finish(id);
  }

  /** 音效关闭或资源不可用时丢弃等待，避免留下无意义的活动 session。 */
  public discardExpectedSound(cue: string): void {
    if (!this.enabled) return;
    const id = this.expectedSoundByCue.get(cue);
    if (!id) return;
    this.expectedSoundByCue.delete(cue);
    this.deleteSession(id);
  }

  private setSession(id: string, session: ScanTimingSession): void {
    this.sessions.set(id, session);
    while (this.sessions.size > MAX_ACTIVE_SESSIONS) {
      const oldest = this.sessions.keys().next().value;
      if (oldest === undefined) break;
      this.deleteSession(oldest);
    }
  }

  /** 记录一个分段：同时输出相对 t0 的总耗时与相对上一分段的增量。 */
  public mark(id: string | undefined, label: string): void {
    if (!this.enabled || id === undefined) return;
    const session = this.sessions.get(id);
    if (!session) return;
    const at = this.now();
    session.marks.push(
      `${label}+${(at - session.startedAt).toFixed(1)}ms` +
        `(d+${(at - session.lastAt).toFixed(1)}ms)`,
    );
    session.lastAt = at;
  }

  /** 输出整条时间线并清理 session；提前丢弃的路径也安全（幂等）。 */
  public finish(id: string | undefined): void {
    if (!this.enabled || id === undefined) return;
    const session = this.sessions.get(id);
    if (!session) return;
    this.deleteSession(id);
    this.log(`[scan-timing] ${session.marks.join(" → ")}`);
  }

  private deleteSession(id: string): void {
    this.sessions.delete(id);
    for (const [cue, expectedId] of this.expectedSoundByCue) {
      if (expectedId === id) this.expectedSoundByCue.delete(cue);
    }
  }
}

export const scanTiming = new ScanTimingCollector();

// 测量期 beacon：确认设备 bundle 确实携带了开关；定位瓶颈后与打点代码一并移除。
if (resolveScanTimingEnabled()) {
  console.log("[scan-timing] enabled on device");
}
