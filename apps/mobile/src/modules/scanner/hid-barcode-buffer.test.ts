import assert from "node:assert/strict";
import { createHidBarcodeKeyBuffer, isHidNativeTextInputFallbackEvent } from "./hid-barcode-buffer";

class FakeTimers {
  private nextId = 1;
  private tasks = new Map<number, () => void>();

  setTimeout = (callback: () => void) => {
    const id = this.nextId;
    this.nextId += 1;
    this.tasks.set(id, callback);
    return id;
  };

  clearTimeout = (id: number) => {
    this.tasks.delete(id);
  };

  runAll() {
    const tasks = Array.from(this.tasks.values());
    this.tasks.clear();
    for (const task of tasks) {
      task();
    }
  }
}

function createBuffer(options?: {
  minLength?: number;
  now?: () => number;
  onFallbackToTextInput?: () => void;
}) {
  const timers = new FakeTimers();
  const scans: string[] = [];
  const buffer = createHidBarcodeKeyBuffer({
    idleMs: 50,
    minLength: options?.minLength ?? 3,
    now: options?.now,
    onBarcode: (barcode) => {
      scans.push(barcode);
    },
    onFallbackToTextInput: options?.onFallbackToTextInput,
    setTimeoutFn: timers.setTimeout as unknown as typeof setTimeout,
    clearTimeoutFn: timers.clearTimeout as unknown as typeof clearTimeout,
  });

  return { buffer, scans, timers };
}

{
  const { buffer, scans, timers } = createBuffer();
  for (const character of "9525813130129") {
    buffer.handleKeyPress({ key: character, character });
  }

  timers.runAll();
  assert.deepEqual(scans, ["9525813130129"], "连续字符应在空闲后提交一次扫码结果");
}

{
  const { buffer, scans, timers } = createBuffer();
  for (const character of "ABC123") {
    buffer.handleKeyPress({ key: character, character });
  }
  buffer.handleKeyPress({ key: "Enter" });
  timers.runAll();

  assert.deepEqual(scans, ["ABC123"], "Enter 应立即提交并清理空闲定时器");
}

{
  const { buffer, scans, timers } = createBuffer();
  for (const character of "2606150113355") {
    buffer.handleKeyPress({ key: character, character });
  }
  buffer.handleKeyPress({ key: "Tab" });
  timers.runAll();

  assert.deepEqual(scans, ["2606150113355"], "Tab 应作为扫码枪结束符立即提交");
}

{
  const { buffer, scans, timers } = createBuffer({ minLength: 4 });
  buffer.handleKeyPress({ key: "A", character: "A" });
  buffer.handleKeyPress({ key: "B", character: "B" });
  buffer.handleKeyPress({ key: "Enter" });
  timers.runAll();

  assert.deepEqual(scans, [], "短条码不应触发查询");
}

{
  let currentTime = 1000;
  const { buffer, scans } = createBuffer({ now: () => currentTime });
  for (const character of "HB313129") {
    buffer.handleKeyPress({ key: character, character });
  }
  buffer.handleKeyPress({ key: "Enter" });

  currentTime += 500;
  for (const character of "HB313129") {
    buffer.handleKeyPress({ key: character, character });
  }
  buffer.handleKeyPress({ key: "Enter" });

  assert.deepEqual(scans, ["HB313129"], "2 秒内重复同条码不应重复触发");
}

{
  let fallbackCount = 0;
  const { buffer, scans, timers } = createBuffer({
    onFallbackToTextInput: () => {
      fallbackCount += 1;
    },
  });

  assert.equal(isHidNativeTextInputFallbackEvent({ key: "12" }), true, "数字 key 且无 character 应判定为 fallback");
  buffer.handleKeyPress({ key: "12" });
  timers.runAll();

  assert.equal(fallbackCount, 1, "数字 key 且无 character 时应切换到隐藏 TextInput 模式");
  assert.deepEqual(scans, [], "fallback 事件不应提交空条码");
}

console.log("hid-barcode-buffer.test.ts: ok");
