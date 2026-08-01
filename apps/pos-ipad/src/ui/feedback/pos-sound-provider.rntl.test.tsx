import { afterEach, beforeEach, expect, jest, test } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";
import * as ExpoAudio from "expo-audio";
import { Pressable, Text, View } from "react-native";

import { usePosSound } from "./pos-sound-context";
import { PosSoundProvider } from "./pos-sound-provider";

import {
  readButtonSoundEnabled,
  readSpecialNodeSoundEnabled,
  saveButtonSoundEnabled,
  saveSpecialNodeSoundEnabled,
} from "@/ui/preferences/terminal-ui-preferences";

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
  pause: ReturnType<typeof jest.fn>;
  play: ReturnType<typeof jest.fn>;
  release: ReturnType<typeof jest.fn>;
  remove: ReturnType<typeof jest.fn>;
  replace: ReturnType<typeof jest.fn>;
  seekTo: ReturnType<typeof jest.fn>;
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

function deferredVoid() {
  let resolve!: () => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<void>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}
