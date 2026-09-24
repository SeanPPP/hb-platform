import assert from "node:assert/strict";
import Module from "node:module";
import React, { act } from "react";

async function run() {
  Object.assign(globalThis, { __DEV__: false, IS_REACT_ACT_ENVIRONMENT: true, React });
  const mockModule = (name: string, exports: object) => {
    const filename = require.resolve(name);
    const module = new Module(filename);
    module.filename = filename;
    module.loaded = true;
    module.exports = exports;
    require.cache[filename] = module;
  };

  const rendererReactFilename = require.resolve("react", { paths: [require.resolve("test-renderer")] });
  if (rendererReactFilename !== require.resolve("react")) mockModule(rendererReactFilename, React);

  let focused = false;
  let blurCount = 0;
  let clearCount = 0;
  let keyboardVisible = false;
  let clock = 1_000;
  const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
  const platform = { OS: "android" };
  const keyboardListeners = new Set<() => void>();
  mockModule("react-native", {
    Platform: platform,
    Keyboard: {
      isVisible: () => keyboardVisible,
      addListener: (event: string, listener: () => void) => {
        assert.equal(event, "keyboardDidHide");
        keyboardListeners.add(listener);
        return { remove: () => keyboardListeners.delete(listener) };
      },
    },
  });

  const { createRoot } = await import("test-renderer");
  const { useVisibleSearchScannerInput } = await import("./use-visible-search-scanner-input");

  const events: string[] = [];
  const fakeInput = {
    clear: () => {
      clearCount++;
    },
    isFocused: () => focused,
    blur: () => {
      if (!focused) return;
      focused = false;
      blurCount++;
    },
  };
  let hook: ReturnType<typeof useVisibleSearchScannerInput<typeof fakeInput>> | null = null;
  function Harness({ value }: { value: string }) {
    hook = useVisibleSearchScannerInput<typeof fakeInput>({
      value,
      onChangeText: (next) => events.push(`input:${next}`),
      onScannerInput: (barcode) => events.push(`scan:${barcode}`),
      burstGapMs: 50,
      now: () => clock,
    });
    hook.searchInputRef.current = fakeInput;
    return null;
  }

  const root = createRoot();
  let scenario = 0;
  const render = async (value: string) => {
    // 每个场景重新挂载，避免上一场景残留的输入值影响增量判断。
    scenario++;
    await act(() => root.render(React.createElement(Harness, { key: scenario, value })));
    focused = true;
    events.length = 0;
  };

  try {
    await render("9528503822120");
    await act(() => hook!.handleChangeText("95285038221209528503822005"));
    assert.deepEqual(events, ["scan:9528503822005"], "Zebra 追加到已聚焦搜索框的条码只提交新条码，不拼接");
    assert.equal(focused, false, "识别扫码后必须释放搜索框焦点，交还隐藏扫码输入框");
    assert.equal(clearCount, 1, "识别扫码后必须直接清空原生输入框，避免条码残留");

    await render("");
    await act(() => hook!.handleChangeText("9528503822107"));
    focused = true;
    await act(() => hook!.handleChangeText("9528503822107"));
    assert.deepEqual(
      events,
      ["scan:9528503822107", "scan:9528503822107"],
      "连续扫同一条码时，上一次已清空输入框，第二次仍须按扫码处理",
    );

    await render("HB038");
    await act(() => hook!.handleChangeText("HB038"));
    assert.equal(events.length, 0, "值未变化的 onChangeText 不做任何处理");

    await render("HB038-12");
    await act(() => hook!.handleChangeText("HB038-129528503822005"));
    assert.deepEqual(events, ["scan:9528503822005"], "旧货号后的完整扫码不得带入旧关键字");

    await render("");
    for (let length = 1; length <= 13; length++) {
      clock += 150;
      await act(() => hook!.handleChangeText("9528503822005".slice(0, length)));
    }
    await act(() => sleep(80));
    assert.equal(events.length, 13);
    assert.ok(events.every((event) => event.startsWith("input:")), "按人手节奏逐字符输入不能误当整条扫码");
    assert.equal(focused, true);

    keyboardVisible = true;
    await render("");
    await act(() => hook!.handleChangeText("9528503822005"));
    assert.deepEqual(events, ["scan:9528503822005"], "Zebra 聚焦搜索框时软键盘总会弹出，整段写入仍须按扫码处理");
    keyboardVisible = false;

    await render("HB038");
    const scanned = "9528503822005";
    for (let length = 1; length <= scanned.length; length++) {
      clock += 5;
      await act(() => hook!.handleChangeText(`HB038${scanned.slice(0, length)}`));
    }
    assert.equal(events.filter((event) => event.startsWith("scan:")).length, 0, "逐字符扫码在停顿前不能提前提交");
    await act(() => sleep(80));
    assert.deepEqual(events.slice(-1), ["scan:9528503822005"], "逐字符快速写入的条码停顿后按扫码提交，且不带旧关键字");
    assert.equal(focused, false);

    await render("");
    for (const text of ["12", "1234"]) {
      clock += 5;
      await act(() => hook!.handleChangeText(text));
    }
    await act(() => sleep(80));
    assert.ok(events.every((event) => event.startsWith("input:")), "快速写入但不足八位时不能当作扫码");

    await render("");
    focused = false;
    await act(() => hook!.handleChangeText("9528503822005"));
    assert.deepEqual(events, ["input:9528503822005"], "搜索框未聚焦时不做扫码识别");

    platform.OS = "ios";
    await render("");
    await act(() => hook!.handleChangeText("9528503822005"));
    assert.deepEqual(events, ["input:9528503822005"], "Android 扫码兼容不能改变 iOS 搜索输入");
    platform.OS = "android";

    await render("");
    const blursBefore = blurCount;
    for (const listener of [...keyboardListeners]) listener();
    assert.equal(blurCount, blursBefore + 1, "收起软键盘后必须释放仍聚焦的搜索框");
    for (const listener of [...keyboardListeners]) listener();
    assert.equal(blurCount, blursBefore + 1, "已失焦时键盘事件不应重复失焦");

    focused = true;
    await act(() => hook!.blurSearchInput());
    assert.equal(focused, false, "提交查询时可主动释放搜索框焦点");
  } finally {
    await act(() => root.unmount());
  }
  assert.equal(keyboardListeners.size, 0, "卸载时移除键盘监听");
  console.log("use-visible-search-scanner-input.test.ts: ok");
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
