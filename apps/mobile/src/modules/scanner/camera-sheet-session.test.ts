import assert from "node:assert/strict";
import test from "node:test";
import {
  createCameraSheetSession,
  isCameraSheetSessionActive,
  reduceCameraSheetSession,
  type CameraSheetSession,
} from "./camera-sheet-session";

test("连续扫码在前景结果出现时暂停，并在结果完成后恢复", () => {
  const scanning = reduceCameraSheetSession(
    createCameraSheetSession("continuous"),
    { type: "open" },
    "continuous",
  );
  const paused = reduceCameraSheetSession(scanning, {
    type: "capture",
    generation: scanning.generation,
  });
  const resumed = reduceCameraSheetSession(paused, {
    type: "foreground-complete",
    focused: true,
    generation: scanning.generation,
  });

  assert.deepEqual(paused, {
    visible: false,
    resumeRequested: true,
    generation: 1,
  });
  assert.deepEqual(resumed, {
    visible: true,
    resumeRequested: true,
    generation: 1,
  });
});

test("连续扫码的两个不同直接命中都无需重新进入相机", () => {
  const opened = reduceCameraSheetSession(
    createCameraSheetSession("continuous"),
    { type: "open" },
    "continuous",
  );
  const firstResult = reduceCameraSheetSession(
    reduceCameraSheetSession(
      opened,
      { type: "capture", generation: opened.generation },
      "continuous",
    ),
    {
      type: "foreground-complete",
      focused: true,
      generation: opened.generation,
    },
    "continuous",
  );
  const secondResult = reduceCameraSheetSession(
    reduceCameraSheetSession(
      firstResult,
      { type: "capture", generation: opened.generation },
      "continuous",
    ),
    {
      type: "foreground-complete",
      focused: true,
      generation: opened.generation,
    },
    "continuous",
  );

  assert.deepEqual(firstResult, {
    visible: true,
    resumeRequested: true,
    generation: 1,
  });
  assert.deepEqual(secondResult, {
    visible: true,
    resumeRequested: true,
    generation: 1,
  });
});

test("单次扫码和主动关闭都不会在异步结果后重新打开相机", () => {
  const singleOpen = reduceCameraSheetSession(
    createCameraSheetSession("single"),
    { type: "open" },
    "single",
  );
  const single = reduceCameraSheetSession(
    singleOpen,
    { type: "capture", generation: singleOpen.generation },
    "single",
  );
  const closed = reduceCameraSheetSession(
    reduceCameraSheetSession(createCameraSheetSession("continuous"), {
      type: "open",
    }),
    { type: "dismiss" },
  );

  assert.deepEqual(
    reduceCameraSheetSession(single, {
      type: "foreground-complete",
      focused: true,
      generation: single.generation,
    }),
    {
      visible: false,
      resumeRequested: false,
      generation: 1,
    },
  );
  assert.deepEqual(
    reduceCameraSheetSession(closed, {
      type: "foreground-complete",
      focused: true,
      generation: 1,
    }),
    closed,
  );
});

test("失焦会取消连续恢复意图", () => {
  const paused: CameraSheetSession = {
    visible: false,
    resumeRequested: true,
    generation: 7,
  };
  assert.deepEqual(reduceCameraSheetSession(paused, { type: "blur" }), {
    visible: false,
    resumeRequested: false,
    generation: 8,
  });
});

test("关闭或失焦后，旧代次不能消费扫码或重新打开相机", () => {
  const opened = reduceCameraSheetSession(
    createCameraSheetSession("continuous"),
    { type: "open" },
    "continuous",
  );
  const closed = reduceCameraSheetSession(
    opened,
    { type: "dismiss" },
    "continuous",
  );
  const blurred = reduceCameraSheetSession(
    opened,
    { type: "blur" },
    "continuous",
  );

  assert.equal(
    reduceCameraSheetSession(
      closed,
      { type: "capture", generation: opened.generation },
      "continuous",
    ),
    closed,
  );
  assert.equal(
    reduceCameraSheetSession(
      closed,
      {
        type: "foreground-complete",
        focused: true,
        generation: opened.generation,
      },
      "continuous",
    ),
    closed,
  );
  assert.equal(
    reduceCameraSheetSession(
      blurred,
      { type: "capture", generation: opened.generation },
      "continuous",
    ),
    blurred,
  );
  assert.equal(
    isCameraSheetSessionActive(closed, opened.generation),
    false,
    "已关闭会话的迟到 callback 不得开始查询或加购",
  );
  assert.equal(
    isCameraSheetSessionActive(blurred, opened.generation),
    false,
    "失焦会话的迟到 callback 不得开始查询或加购",
  );
});
