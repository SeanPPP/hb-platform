import { afterEach, beforeEach, expect, jest, test } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";
import * as ExpoAudio from "expo-audio";
import { Platform, Pressable, Text, View } from "react-native";

import { usePosSound } from "./pos-sound-context";
import { PosSoundProvider } from "./pos-sound-provider";

import {
  readButtonSoundEnabled,
  readSpecialNodeSoundEnabled,
  saveButtonSoundEnabled,
  saveSpecialNodeSoundEnabled,
} from "@/ui/preferences/terminal-ui-preferences";

const mockSoundPlaying = jest.fn();
const mockDiscardExpectedSound = jest.fn();

jest.mock("@/features/sales/runtime/scan-timing", () => ({
  scanTiming: {
    discardExpectedSound: (cue: string) => mockDiscardExpectedSound(cue),
    soundPlaying: (cue: string) => mockSoundPlaying(cue),
  },
}));

jest.mock("@/ui/preferences/terminal-ui-preferences", () => ({
  readButtonSoundEnabled: jest.fn(),
  readSpecialNodeSoundEnabled: jest.fn(),
  saveButtonSoundEnabled: jest.fn(),
  saveSpecialNodeSoundEnabled: jest.fn(),
}));

const mockReadButtonSoundEnabled = jest.mocked(readButtonSoundEnabled);
const mockReadSpecialNodeSoundEnabled = jest.mocked(
  readSpecialNodeSoundEnabled,
);
const mockSaveButtonSoundEnabled = jest.mocked(saveButtonSoundEnabled);
const mockSaveSpecialNodeSoundEnabled = jest.mocked(
  saveSpecialNodeSoundEnabled,
);

type MockAudioPlayer = {
  currentStatus: { isLoaded: boolean; playbackState?: string };
  isLoaded: boolean;
  volume: number;
  pause: ReturnType<typeof jest.fn>;
  play: ReturnType<typeof jest.fn>;
  release: ReturnType<typeof jest.fn>;
  remove: ReturnType<typeof jest.fn>;
  replace: ReturnType<typeof jest.fn>;
  seekTo: ReturnType<typeof jest.fn>;
  addListener: ReturnType<typeof jest.fn>;
  __emitStatus: ReturnType<typeof jest.fn>;
};

const {
  __mockAudioPlayer,
  __mockAudioPlayers,
  __resetAudioMock,
} = ExpoAudio as typeof ExpoAudio & {
  __mockAudioPlayer: MockAudioPlayer;
  __mockAudioPlayers: MockAudioPlayer[];
  __resetAudioMock(): void;
};

function preloadedPlayer(index: number): MockAudioPlayer {
  const player = __mockAudioPlayers[index];
  if (!player) throw new Error(`缺少预载播放器 ${index}`);
  return player;
}

function SoundProbe() {
  const {
    buttonSoundEnabled,
    play,
    setButtonSoundEnabled,
    setSpecialNodeSoundEnabled,
    specialNodeSoundEnabled,
  } = usePosSound();

  return (
    <View>
      <Text testID="button-sound-enabled">{String(buttonSoundEnabled)}</Text>
      <Text testID="special-sound-enabled">
        {String(specialNodeSoundEnabled)}
      </Text>
      <Pressable onPress={() => play("tap")} testID="sound-play-tap" />
      <Pressable
        onPress={() => play("query-found")}
        testID="sound-play-result"
      />
      <Pressable
        onPress={() => play("cart-added")}
        testID="sound-play-cart-added"
      />
      <Pressable
        onPress={() => setButtonSoundEnabled(false)}
        testID="button-sound-disable"
      />
      <Pressable
        onPress={() => setButtonSoundEnabled(true)}
        testID="button-sound-enable"
      />
      <Pressable
        onPress={() => setSpecialNodeSoundEnabled(false)}
        testID="special-sound-disable"
      />
      <Pressable
        onPress={() => setSpecialNodeSoundEnabled(true)}
        testID="special-sound-enable"
      />
    </View>
  );
}

const originalPlatform = Platform.OS;

function setPlatform(os: "android" | "ios" | "web") {
  Object.defineProperty(Platform, "OS", { configurable: true, value: os });
}

beforeEach(() => {
  __resetAudioMock();
  jest.clearAllMocks();
  mockReadButtonSoundEnabled.mockReturnValue(true);
  mockReadSpecialNodeSoundEnabled.mockReturnValue(true);
  mockSaveButtonSoundEnabled.mockResolvedValue(undefined);
  mockSaveSpecialNodeSoundEnabled.mockResolvedValue(undefined);
});

afterEach(() => {
  jest.restoreAllMocks();
  Object.defineProperty(Platform, "OS", {
    configurable: true,
    value: originalPlatform,
  });
});

test("Provider 根级预载全部 cue、保留音频会话，并把同类点击从头重播", async () => {
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );

  await waitFor(() =>
    expect(ExpoAudio.setAudioModeAsync).toHaveBeenCalledWith({
      allowsRecording: false,
      interruptionMode: "mixWithOthers",
      playsInSilentMode: true,
      shouldPlayInBackground: false,
    }),
  );
  expect(ExpoAudio.createAudioPlayer).toHaveBeenCalledTimes(11);

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  await waitFor(() => expect(preloadedPlayer(0).play).toHaveBeenCalledTimes(1));
  await fireEvent.press(screen.getByTestId("sound-play-tap"));

  expect(preloadedPlayer(0).replace).not.toHaveBeenCalled();
  expect(preloadedPlayer(0).seekTo).toHaveBeenCalledTimes(2);
  expect(preloadedPlayer(0).pause).toHaveBeenCalledTimes(1);
  await waitFor(() => expect(preloadedPlayer(0).play).toHaveBeenCalledTimes(2));
});

test.each([
  ["两组开启", true, true, 1, 1],
  ["仅普通按钮开启", true, false, 1, 0],
  ["仅特殊节点开启", false, true, 0, 1],
  ["两组关闭", false, false, 0, 0],
] as const)(
  "%s时仅播放对应组 cue",
  async (
    _caseName,
    buttonEnabled,
    specialEnabled,
    expectedButtonPlays,
    expectedSpecialPlays,
  ) => {
    mockReadButtonSoundEnabled.mockReturnValue(buttonEnabled);
    mockReadSpecialNodeSoundEnabled.mockReturnValue(specialEnabled);
    const screen = await render(
      <PosSoundProvider>
        <SoundProbe />
      </PosSoundProvider>,
    );

    expect(screen.getByTestId("button-sound-enabled").props.children).toBe(
      String(buttonEnabled),
    );
    expect(screen.getByTestId("special-sound-enabled").props.children).toBe(
      String(specialEnabled),
    );

    await fireEvent.press(screen.getByTestId("sound-play-tap"));
    if (buttonEnabled) {
      await waitFor(() =>
        expect(preloadedPlayer(0).play).toHaveBeenCalledTimes(1),
      );
    }

    await fireEvent.press(screen.getByTestId("sound-play-result"));
    if (specialEnabled) {
      await waitFor(() =>
        expect(preloadedPlayer(4).play).toHaveBeenCalledTimes(1),
      );
    } else {
      await Promise.resolve();
    }

    expect(preloadedPlayer(0).seekTo).toHaveBeenCalledTimes(
      expectedButtonPlays,
    );
    expect(preloadedPlayer(0).play).toHaveBeenCalledTimes(expectedButtonPlays);
    expect(preloadedPlayer(4).seekTo).toHaveBeenCalledTimes(
      expectedSpecialPlays,
    );
    expect(preloadedPlayer(4).play).toHaveBeenCalledTimes(
      expectedSpecialPlays,
    );
  },
);

test("两组独立保存，且仅从关闭切到开启时试听各自代表音", async () => {
  mockReadButtonSoundEnabled.mockReturnValue(false);
  mockReadSpecialNodeSoundEnabled.mockReturnValue(false);
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );

  await fireEvent.press(screen.getByTestId("button-sound-disable"));
  await fireEvent.press(screen.getByTestId("special-sound-disable"));
  expect(preloadedPlayer(0).seekTo).not.toHaveBeenCalled();
  expect(preloadedPlayer(7).seekTo).not.toHaveBeenCalled();

  await fireEvent.press(screen.getByTestId("button-sound-enable"));
  await waitFor(() => expect(preloadedPlayer(0).play).toHaveBeenCalledTimes(1));
  await fireEvent.press(screen.getByTestId("button-sound-enable"));
  await Promise.resolve();
  expect(preloadedPlayer(0).play).toHaveBeenCalledTimes(1);

  await fireEvent.press(screen.getByTestId("special-sound-enable"));
  await waitFor(() => expect(preloadedPlayer(7).play).toHaveBeenCalledTimes(1));
  await fireEvent.press(screen.getByTestId("special-sound-enable"));
  await fireEvent.press(screen.getByTestId("special-sound-disable"));
  await fireEvent.press(screen.getByTestId("button-sound-disable"));
  await Promise.resolve();

  expect(preloadedPlayer(0).play).toHaveBeenCalledTimes(1);
  expect(preloadedPlayer(7).play).toHaveBeenCalledTimes(1);
  expect(mockSaveButtonSoundEnabled).toHaveBeenNthCalledWith(1, false);
  expect(mockSaveButtonSoundEnabled).toHaveBeenNthCalledWith(2, true);
  expect(mockSaveButtonSoundEnabled).toHaveBeenNthCalledWith(3, true);
  expect(mockSaveButtonSoundEnabled).toHaveBeenNthCalledWith(4, false);
  expect(mockSaveSpecialNodeSoundEnabled).toHaveBeenNthCalledWith(1, false);
  expect(mockSaveSpecialNodeSoundEnabled).toHaveBeenNthCalledWith(2, true);
  expect(mockSaveSpecialNodeSoundEnabled).toHaveBeenNthCalledWith(3, true);
  expect(mockSaveSpecialNodeSoundEnabled).toHaveBeenNthCalledWith(4, false);
});

test("同组 cue 的 seek 未完成时关闭该组，晚到回调不会反播", async () => {
  const pendingSeek = deferredVoid();
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  preloadedPlayer(4).seekTo.mockReturnValueOnce(pendingSeek.promise);

  await fireEvent.press(screen.getByTestId("sound-play-result"));
  await fireEvent.press(screen.getByTestId("special-sound-disable"));

  expect(preloadedPlayer(4).pause).toHaveBeenCalledTimes(1);
  expect(screen.getByTestId("special-sound-enabled").props.children).toBe(
    "false",
  );
  expect(screen.getByTestId("button-sound-enabled").props.children).toBe(
    "true",
  );
  pendingSeek.resolve();
  await Promise.resolve();

  expect(preloadedPlayer(4).play).not.toHaveBeenCalled();
  expect(preloadedPlayer(0).play).not.toHaveBeenCalled();
});

test("关闭另一组不会取消当前特殊节点的待完成播放", async () => {
  const pendingSeek = deferredVoid();
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  preloadedPlayer(4).seekTo.mockReturnValueOnce(pendingSeek.promise);

  await fireEvent.press(screen.getByTestId("sound-play-result"));
  await fireEvent.press(screen.getByTestId("button-sound-disable"));
  expect(preloadedPlayer(4).pause).not.toHaveBeenCalled();

  pendingSeek.resolve();
  await waitFor(() => expect(preloadedPlayer(4).play).toHaveBeenCalledTimes(1));
  expect(mockSaveButtonSoundEnabled).toHaveBeenCalledWith(false);
  expect(mockSaveSpecialNodeSoundEnabled).not.toHaveBeenCalled();
});

test("快速切换跨组 cue 时旧 seek 不能反播，单一 lane 只播放最后 cue", async () => {
  const firstSeek = deferredVoid();
  const secondSeek = deferredVoid();
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  preloadedPlayer(0).seekTo.mockReturnValueOnce(firstSeek.promise);
  preloadedPlayer(4).seekTo.mockReturnValueOnce(secondSeek.promise);

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  await fireEvent.press(screen.getByTestId("sound-play-result"));
  expect(preloadedPlayer(0).pause).toHaveBeenCalledTimes(1);

  secondSeek.resolve();
  await waitFor(() => expect(preloadedPlayer(4).play).toHaveBeenCalledTimes(1));
  firstSeek.resolve();
  await Promise.resolve();

  expect(preloadedPlayer(0).play).not.toHaveBeenCalled();
});

test("seek 被原生层拒绝时安全 no-op", async () => {
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  preloadedPlayer(0).seekTo.mockRejectedValueOnce(new Error("seek failed"));
  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  await Promise.resolve();

  expect(preloadedPlayer(0).play).not.toHaveBeenCalled();
});

test("Provider 外两组均关闭且所有操作保持 no-op", async () => {
  const screen = await render(<SoundProbe />);
  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  await fireEvent.press(screen.getByTestId("sound-play-result"));
  await fireEvent.press(screen.getByTestId("button-sound-enable"));
  await fireEvent.press(screen.getByTestId("special-sound-enable"));

  expect(screen.getByTestId("button-sound-enabled").props.children).toBe(
    "false",
  );
  expect(screen.getByTestId("special-sound-enabled").props.children).toBe(
    "false",
  );
  expect(__mockAudioPlayer.play).not.toHaveBeenCalled();
  expect(mockSaveButtonSoundEnabled).not.toHaveBeenCalled();
  expect(mockSaveSpecialNodeSoundEnabled).not.toHaveBeenCalled();
});

test("卸载会取消晚到 seek，并暂停、释放全部预载播放器", async () => {
  const pendingSeek = deferredVoid();
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  preloadedPlayer(0).seekTo.mockReturnValueOnce(pendingSeek.promise);

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  screen.unmount();

  expect(__mockAudioPlayers).toHaveLength(11);
  await waitFor(() => {
    for (const player of __mockAudioPlayers) {
      expect(player.pause).toHaveBeenCalledTimes(1);
      expect(player.release).toHaveBeenCalledTimes(1);
      expect(player.remove).not.toHaveBeenCalled();
    }
  });

  pendingSeek.resolve();
  await Promise.resolve();
  expect(preloadedPlayer(0).play).not.toHaveBeenCalled();
});

test("冷启动首个音效：isLoaded=false 时不播放，load event 后恰好播放一次", async () => {
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(0);
  player.isLoaded = false;
  player.currentStatus = { isLoaded: false, playbackState: "buffering" };

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  await Promise.resolve();

  expect(player.seekTo).not.toHaveBeenCalled();
  expect(player.play).not.toHaveBeenCalled();

  player.__emitStatus({ isLoaded: true });

  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(1));
  expect(player.seekTo).toHaveBeenCalledTimes(1);
});

test("Android 媒体输出冷启动先静音预热，结束后只登记一次真实购物车提示音", async () => {
  setPlatform("android");
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(7);
  const warmupPlayer = preloadedPlayer(0);

  await fireEvent.press(screen.getByTestId("sound-play-cart-added"));

  await waitFor(() => expect(warmupPlayer.play).toHaveBeenCalledTimes(1));
  expect(player.play).not.toHaveBeenCalled();
  expect(warmupPlayer.volume).toBeLessThan(0.01);
  // 模拟 TC26 冷输出：play() 返回后约 100ms 才真正进入 playing。
  await new Promise((resolve) => setTimeout(resolve, 100));
  warmupPlayer.__emitStatus({ isLoaded: true, playing: true });
  expect(mockSoundPlaying).not.toHaveBeenCalled();

  warmupPlayer.__emitStatus({
    didJustFinish: true,
    isLoaded: true,
    playbackState: "ended",
    playing: false,
  });

  // 预热等待必须从原生 playing 开始，而不是从过早返回的 play() 开始。
  await new Promise((resolve) => setTimeout(resolve, 100));
  expect(player.play).not.toHaveBeenCalled();
  await new Promise((resolve) => setTimeout(resolve, 120));

  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(1));
  expect(player.seekTo).toHaveBeenCalledTimes(1);
  expect(warmupPlayer.pause).toHaveBeenCalledTimes(2);
  expect(warmupPlayer.pause.mock.invocationCallOrder[0]!).toBeLessThan(
    warmupPlayer.seekTo.mock.invocationCallOrder[0]!,
  );
  expect(warmupPlayer.volume).toBe(1);
  expect(player.volume).toBe(1);

  player.__emitStatus({ isLoaded: true, playing: true });
  expect(mockSoundPlaying).toHaveBeenCalledTimes(1);
  expect(mockSoundPlaying).toHaveBeenCalledWith("cart-added");
});

test("Android 预热窗口内的后续购物车提示音直接播放，不重复预热", async () => {
  setPlatform("android");
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(7);
  const warmupPlayer = preloadedPlayer(0);

  await fireEvent.press(screen.getByTestId("sound-play-cart-added"));
  await waitFor(() => expect(warmupPlayer.play).toHaveBeenCalledTimes(1));
  warmupPlayer.__emitStatus({ isLoaded: true, playing: true });
  warmupPlayer.__emitStatus({
    didJustFinish: true,
    isLoaded: true,
    playbackState: "ended",
    playing: false,
  });
  await new Promise((resolve) => setTimeout(resolve, 220));
  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(1));

  await fireEvent.press(screen.getByTestId("sound-play-cart-added"));

  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(2));
  expect(player.seekTo).toHaveBeenCalledTimes(2);
  expect(warmupPlayer.play).toHaveBeenCalledTimes(1);
  expect(player.volume).toBe(1);
});

test("Android 预热期间禁用音效会取消兜底，结束事件不会迟到反播", async () => {
  setPlatform("android");
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(7);
  const warmupPlayer = preloadedPlayer(0);

  await fireEvent.press(screen.getByTestId("sound-play-cart-added"));
  await waitFor(() => expect(warmupPlayer.play).toHaveBeenCalledTimes(1));
  expect(player.play).not.toHaveBeenCalled();
  expect(warmupPlayer.volume).toBeLessThan(0.01);
  warmupPlayer.__emitStatus({ isLoaded: true, playing: true });

  await fireEvent.press(screen.getByTestId("special-sound-disable"));
  expect(warmupPlayer.volume).toBe(1);
  warmupPlayer.__emitStatus({
    didJustFinish: true,
    isLoaded: true,
    playbackState: "ended",
    playing: false,
  });
  await new Promise((resolve) => setTimeout(resolve, 220));

  expect(player.play).not.toHaveBeenCalled();
  expect(mockDiscardExpectedSound).toHaveBeenCalledWith("cart-added");
});

test("Android 预热收不到 playing 状态时使用 500ms 兜底播放真实提示音", async () => {
  setPlatform("android");
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(7);
  const warmupPlayer = preloadedPlayer(0);

  await fireEvent.press(screen.getByTestId("sound-play-cart-added"));
  await waitFor(() => expect(warmupPlayer.play).toHaveBeenCalledTimes(1));
  expect(player.play).not.toHaveBeenCalled();
  expect(warmupPlayer.volume).toBeLessThan(0.01);

  await new Promise((resolve) => setTimeout(resolve, 550));

  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(1));
  expect(player.seekTo).toHaveBeenCalledTimes(1);
  expect(warmupPlayer.volume).toBe(1);
  expect(player.volume).toBe(1);
});

test("已加载玩家请求后即时播放", async () => {
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(0);

  expect(player.isLoaded).toBe(true);
  await fireEvent.press(screen.getByTestId("sound-play-tap"));

  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(1));
  expect(player.seekTo).toHaveBeenCalledTimes(1);
});

test("Android 短音效结束后 isLoaded=false 时仍可从头重播", async () => {
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(0);

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(1));

  // expo-audio Android 在 STATE_ENDED 时属性 isLoaded=false，
  // 但 currentStatus 会把已结束且可 seek 的播放器标记为 loaded。
  player.isLoaded = false;
  player.currentStatus = { isLoaded: true, playbackState: "ended" };

  await fireEvent.press(screen.getByTestId("sound-play-tap"));

  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(2));
  expect(player.seekTo).toHaveBeenCalledTimes(2);
});

test("原生播放器进入 playing 后登记对应 cue 的端到端计时终点", async () => {
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(0);

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(1));
  player.__emitStatus({ isLoaded: true, playing: true });

  expect(mockSoundPlaying).toHaveBeenCalledWith("tap");
});

test("新 cue 覆盖未加载的 pending 请求，旧 pending 不反播", async () => {
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const tapPlayer = preloadedPlayer(0);
  const resultPlayer = preloadedPlayer(4);
  tapPlayer.isLoaded = false;
  tapPlayer.currentStatus = { isLoaded: false, playbackState: "buffering" };
  resultPlayer.isLoaded = false;
  resultPlayer.currentStatus = { isLoaded: false, playbackState: "buffering" };

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  await fireEvent.press(screen.getByTestId("sound-play-result"));

  expect(mockDiscardExpectedSound).toHaveBeenCalledWith("tap");

  tapPlayer.__emitStatus({ isLoaded: true });
  await Promise.resolve();
  expect(tapPlayer.play).not.toHaveBeenCalled();

  resultPlayer.__emitStatus({ isLoaded: true });
  await waitFor(() => expect(resultPlayer.play).toHaveBeenCalledTimes(1));
  expect(tapPlayer.play).not.toHaveBeenCalled();
});

test("分组禁用会取消未加载的 pending，迟到 load 不反播", async () => {
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(4);
  player.isLoaded = false;
  player.currentStatus = { isLoaded: false, playbackState: "buffering" };

  await fireEvent.press(screen.getByTestId("sound-play-result"));
  await fireEvent.press(screen.getByTestId("special-sound-disable"));
  player.__emitStatus({ isLoaded: true });
  await Promise.resolve();

  expect(player.play).not.toHaveBeenCalled();
});

test("卸载会取消未加载的 pending，迟到 load 不反播", async () => {
  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(0);
  player.isLoaded = false;
  player.currentStatus = { isLoaded: false, playbackState: "buffering" };

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  screen.unmount();
  player.__emitStatus({ isLoaded: true });
  await Promise.resolve();

  expect(player.play).not.toHaveBeenCalled();
});

test("音频模式就绪前请求保持 pending，模式就绪后才播放", async () => {
  const pendingMode = deferredVoid();
  jest
    .mocked(ExpoAudio.setAudioModeAsync)
    .mockReturnValueOnce(pendingMode.promise);

  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(0);

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  await Promise.resolve();
  expect(player.seekTo).not.toHaveBeenCalled();
  expect(player.play).not.toHaveBeenCalled();

  pendingMode.resolve();
  await waitFor(() => expect(player.play).toHaveBeenCalledTimes(1));
  expect(player.seekTo).toHaveBeenCalledTimes(1);
});

test("音频模式初始化失败时安全 no-op，不影响业务", async () => {
  const pendingMode = deferredVoid();
  jest
    .mocked(ExpoAudio.setAudioModeAsync)
    .mockReturnValueOnce(pendingMode.promise);

  const screen = await render(
    <PosSoundProvider>
      <SoundProbe />
    </PosSoundProvider>,
  );
  const player = preloadedPlayer(0);

  await fireEvent.press(screen.getByTestId("sound-play-tap"));
  pendingMode.reject(new Error("audio mode failed"));
  await Promise.resolve();

  expect(player.play).not.toHaveBeenCalled();
  expect(player.seekTo).not.toHaveBeenCalled();
});

function deferredVoid() {
  let resolve!: () => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<void>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}
