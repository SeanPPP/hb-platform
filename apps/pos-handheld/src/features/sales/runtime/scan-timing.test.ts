import assert from "node:assert/strict";
import test from "node:test";

import { ScanTimingCollector } from "./scan-timing";

/** 可手动拨动的假时钟，让耗时断言完全确定。 */
function createFakeClock(): () => number {
  let value = 0;
  return () => {
    value += 100;
    return value;
  };
}

test("未开启时所有方法为空操作且不输出日志", () => {
  const lines: string[] = [];
  const timing = new ScanTimingCollector(false, createFakeClock(), (line) =>
    lines.push(line),
  );
  timing.begin("scan-1", "presenter-enter");
  timing.mark("scan-1", "runtime-enter");
  timing.finish("scan-1");
  assert.deepEqual(lines, []);
});

test("开启时 finish 输出包含总耗时与分段增量的时间线", () => {
  const lines: string[] = [];
  const timing = new ScanTimingCollector(true, createFakeClock(), (line) =>
    lines.push(line),
  );
  timing.begin("scan-1", "presenter-enter");
  timing.mark("scan-1", "authorized");
  timing.mark("scan-1", "findExact-done");
  timing.finish("scan-1");
  assert.equal(lines.length, 1);
  const line = lines[0];
  assert.ok(line);
  // 假时钟每次 +100ms：总耗时 100/200ms，分段增量各 100ms。
  assert.match(line, /^\[scan-timing\] presenter-enter → /);
  assert.match(line, /authorized\+100\.0ms\(d\+100\.0ms\)/);
  assert.match(line, /findExact-done\+200\.0ms\(d\+100\.0ms\)/);
});

test("finish 幂等：同一 session 只输出一次", () => {
  const lines: string[] = [];
  const timing = new ScanTimingCollector(true, createFakeClock(), (line) =>
    lines.push(line),
  );
  timing.begin("scan-1", "presenter-enter");
  timing.finish("scan-1");
  timing.finish("scan-1");
  assert.equal(lines.length, 1);
});

test("id 为 undefined 时静默忽略，不会污染 session 表", () => {
  const lines: string[] = [];
  const timing = new ScanTimingCollector(true, createFakeClock(), (line) =>
    lines.push(line),
  );
  timing.begin(undefined, "presenter-enter");
  timing.mark(undefined, "authorized");
  timing.finish(undefined);
  assert.equal(lines.length, 0);
});

test("session 数量超过上限时丢弃最旧记录，防止内存无限增长", () => {
  const lines: string[] = [];
  const timing = new ScanTimingCollector(true, createFakeClock(), (line) =>
    lines.push(line),
  );
  // 上限为 32：先塞满。
  for (let i = 0; i < 32; i += 1) {
    timing.begin(`scan-${i}`, "presenter-enter");
  }
  // 再开一个新 session，最旧的 scan-0 被淘汰，finish 时无输出。
  timing.begin("scan-new", "presenter-enter");
  timing.finish("scan-0");
  assert.equal(lines.length, 0);
  // 被保留的 session 仍可正常输出。
  timing.finish("scan-new");
  assert.equal(lines.length, 1);
});

test("HID 时间线从最后字符开始，并在对应音效真正播放后结束", () => {
  let nowMs = 1_000;
  const lines: string[] = [];
  const timing = new ScanTimingCollector(
    true,
    () => nowMs,
    (line) => lines.push(line),
  );

  timing.noteHidCharacter();
  nowMs += 85;
  timing.beginHid("scan-1");
  nowMs += 40;
  timing.mark("scan-1", "cart-published");
  timing.expectSound("scan-1", "cart-added");
  nowMs += 25;
  timing.soundPlaying("cart-added");

  assert.deepEqual(lines, [
    "[scan-timing] last-character → hid-submit+85.0ms(d+85.0ms) → " +
      "cart-published+125.0ms(d+40.0ms) → " +
      "sound-playing+150.0ms(d+25.0ms)",
  ]);
});

test("同 cue 的新请求替换旧请求，禁用音效可丢弃等待中的时间线", () => {
  let nowMs = 0;
  const lines: string[] = [];
  const timing = new ScanTimingCollector(
    true,
    () => nowMs,
    (line) => lines.push(line),
  );

  timing.noteHidCharacter();
  nowMs = 85;
  timing.beginHid("scan-old");
  timing.expectSound("scan-old", "cart-added");

  nowMs = 100;
  timing.noteHidCharacter();
  nowMs = 185;
  timing.beginHid("scan-new");
  timing.expectSound("scan-new", "cart-added");
  nowMs = 200;
  timing.soundPlaying("cart-added");

  timing.noteHidCharacter();
  nowMs = 300;
  timing.beginHid("scan-disabled");
  timing.expectSound("scan-disabled", "cart-added");
  timing.discardExpectedSound("cart-added");
  timing.soundPlaying("cart-added");

  assert.equal(lines.length, 1);
  assert.match(lines[0] ?? "", /last-character.*sound-playing/);
  assert.doesNotMatch(lines[0] ?? "", /scan-old|scan-disabled/);
});
