import {
  createAudioPlayer,
  setAudioModeAsync,
  type AudioPlayer,
  type AudioStatus,
} from "expo-audio";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Platform } from "react-native";

import {
  PosSoundContext,
  type PosSoundContextValue,
  type PosSoundCue,
} from "./pos-sound-context";

import { scanTiming } from "@/features/sales/runtime/scan-timing";
import {
  readButtonSoundEnabled,
  readSpecialNodeSoundEnabled,
  saveButtonSoundEnabled,
  saveSpecialNodeSoundEnabled,
} from "@/ui/preferences/terminal-ui-preferences";

type PosSoundGroup = "button" | "special-node";

const SOUND_GROUP_BY_CUE: Readonly<Record<PosSoundCue, PosSoundGroup>> = {
  tap: "button",
  key: "button",
  navigate: "button",
  danger: "button",
  "query-found": "special-node",
  "query-empty": "special-node",
  "query-error": "special-node",
  "cart-added": "special-node",
  "cart-incremented": "special-node",
  "cart-not-found": "special-node",
  "cart-failed-blocked": "special-node",
};

const AUDIO_MODE = {
  allowsRecording: false,
  interruptionMode: "mixWithOthers",
  playsInSilentMode: true,
  shouldPlayInBackground: false,
} as const;

// TC26 的媒体输出空闲约 3 秒后会休眠，首个 70–130ms 短音可能全部落在功放唤醒期。
// 先以不可听但非零的音量走完整条输出链，再播放一次满音量提示音。
const ANDROID_OUTPUT_WARM_WINDOW_MS = 2_500;
const ANDROID_WARMUP_DELAY_MS = 180;
const ANDROID_WARMUP_START_FALLBACK_MS = 500;
const ANDROID_WARMUP_VOLUME = 0.001;

const SOURCES: Readonly<Record<PosSoundCue, number>> = {
  tap: require("../../../assets/sounds/tap.wav"),
  key: require("../../../assets/sounds/key.wav"),
  navigate: require("../../../assets/sounds/navigate.wav"),
  danger: require("../../../assets/sounds/danger.wav"),
  "query-found": require("../../../assets/sounds/query-found.wav"),
  "query-empty": require("../../../assets/sounds/query-empty.wav"),
  "query-error": require("../../../assets/sounds/query-error.wav"),
  "cart-added": require("../../../assets/sounds/cart-added.wav"),
  "cart-incremented": require("../../../assets/sounds/cart-incremented.wav"),
  "cart-not-found": require("../../../assets/sounds/cart-not-found.wav"),
  "cart-failed-blocked": require("../../../assets/sounds/cart-failed-blocked.wav"),
};

function isPlayerReadyToReplay(player: AudioPlayer): boolean {
  try {
    // expo-audio Android 在短音效进入 STATE_ENDED 后，isLoaded 属性会变为 false，
    // 但 currentStatus.isLoaded 仍正确表示该 player 已加载且可以 seek 后重播。
    return player.isLoaded || player.currentStatus.isLoaded;
  } catch {
    return false;
  }
}

type ActivePlayback = {
  cue: PosSoundCue;
  group: PosSoundGroup;
  groupVersion: number;
  phase:
    | "pending"
    | "preparing"
    | "warming"
    | "audible-preparing"
    | "audible";
  player: AudioPlayer;
  sequence: number;
  warmupObservedPlaying: boolean;
  warmupPlayer: AudioPlayer | null;
  warmupTimer: ReturnType<typeof setTimeout> | null;
};

export function PosSoundProvider({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const [buttonSoundEnabled, setButtonSoundEnabledState] = useState(
    readButtonSoundEnabled,
  );
  const [specialNodeSoundEnabled, setSpecialNodeSoundEnabledState] = useState(
    readSpecialNodeSoundEnabled,
  );
  const enabledByGroup = useRef<Record<PosSoundGroup, boolean>>({
    button: buttonSoundEnabled,
    "special-node": specialNodeSoundEnabled,
  });
  const groupPlaybackVersion = useRef<Record<PosSoundGroup, number>>({
    button: 0,
    "special-node": 0,
  });
  const players = useRef<Partial<Record<PosSoundCue, AudioPlayer>>>({});
  const activePlayback = useRef<ActivePlayback | null>(null);
  const playbackSequence = useRef(0);
  const mounted = useRef(false);
  const audioModePromise = useRef<Promise<boolean> | null>(null);
  const androidOutputWarmUntil = useRef(0);

  const ensureAudioMode = useCallback((): Promise<boolean> => {
    if (!audioModePromise.current) {
      audioModePromise.current = setAudioModeAsync(AUDIO_MODE).then(
        () => true,
        () => false,
      );
    }
    return audioModePromise.current;
  }, []);

  const isRequestCurrent = useCallback(
    (request: ActivePlayback): boolean =>
      mounted.current &&
      activePlayback.current === request &&
      request.sequence === playbackSequence.current &&
      request.groupVersion === groupPlaybackVersion.current[request.group] &&
      enabledByGroup.current[request.group] &&
      players.current[request.cue] === request.player,
    [],
  );

  const restoreFullVolume = useCallback((player: AudioPlayer): void => {
    try {
      player.volume = 1;
    } catch {
      // 恢复音量失败不能阻断后续业务或其他播放器释放。
    }
  }, []);

  const clearWarmupTimer = useCallback((request: ActivePlayback): void => {
    if (!request.warmupTimer) return;
    clearTimeout(request.warmupTimer);
    request.warmupTimer = null;
  }, []);

  const stopAndroidWarmup = useCallback(
    (request: ActivePlayback): void => {
      const warmupPlayer = request.warmupPlayer;
      if (!warmupPlayer) return;
      request.warmupPlayer = null;
      try {
        warmupPlayer.pause();
      } catch {
        // 预热播放器停止失败不能阻断目标提示音或取消流程。
      }
      restoreFullVolume(warmupPlayer);
    },
    [restoreFullVolume],
  );

  const playAudible = useCallback(
    (request: ActivePlayback) => {
      if (
        !isRequestCurrent(request) ||
        request.phase === "audible-preparing" ||
        request.phase === "audible"
      ) {
        return;
      }
      stopAndroidWarmup(request);
      request.phase = "audible-preparing";
      clearWarmupTimer(request);
      restoreFullVolume(request.player);

      void (async () => {
        try {
          await request.player.seekTo(0);
        } catch {
          if (isRequestCurrent(request)) {
            scanTiming.discardExpectedSound(request.cue);
          }
          return;
        }

        if (!isRequestCurrent(request)) return;
        request.phase = "audible";
        try {
          request.player.play();
        } catch {
          // 播放失败不得影响对应的业务事件。
          scanTiming.discardExpectedSound(request.cue);
        }
      })();
    },
    [
      clearWarmupTimer,
      isRequestCurrent,
      restoreFullVolume,
      stopAndroidWarmup,
    ],
  );

  const finishAndroidWarmup = useCallback(
    (request: ActivePlayback) => {
      if (!isRequestCurrent(request) || request.phase !== "warming") return;
      clearWarmupTimer(request);
      androidOutputWarmUntil.current =
        Date.now() + ANDROID_OUTPUT_WARM_WINDOW_MS;
      playAudible(request);
    },
    [clearWarmupTimer, isRequestCurrent, playAudible],
  );

  const startPlayback = useCallback(
    (request: ActivePlayback) => {
      if (request.phase !== "pending") return;
      request.phase = "preparing";
      void (async () => {
        const modeReady = await ensureAudioMode();
        if (!modeReady) {
          if (isRequestCurrent(request)) {
            scanTiming.discardExpectedSound(request.cue);
          }
          return;
        }
        if (!isRequestCurrent(request)) return;

        const shouldWarmAndroidOutput =
          Platform.OS === "android" &&
          Date.now() >= androidOutputWarmUntil.current;
        if (!shouldWarmAndroidOutput) {
          playAudible(request);
          return;
        }

        // 使用另一个已预载 player 唤醒媒体输出；目标 player 从未进入 ended，
        // 避免 TC26 吞掉同一 ExoPlayer 的首次 seek+play 重播。
        const warmupPlayer =
          request.player === players.current.tap
            ? players.current.key
            : players.current.tap;
        if (!warmupPlayer) {
          playAudible(request);
          return;
        }
        request.warmupPlayer = warmupPlayer;

        try {
          warmupPlayer.pause();
          warmupPlayer.volume = ANDROID_WARMUP_VOLUME;
        } catch {
          playAudible(request);
          return;
        }

        try {
          await warmupPlayer.seekTo(0);
        } catch {
          if (isRequestCurrent(request)) playAudible(request);
          return;
        }

        if (!isRequestCurrent(request)) return;
        request.phase = "warming";
        try {
          warmupPlayer.play();
          // TC26 冷输出时 play() 会比原生 AudioTrack 真正 started 早约 100–260ms。
          // 正常路径由 playing 状态重新计时；此处仅保留无状态回调时的兜底。
          request.warmupTimer = setTimeout(
            () => finishAndroidWarmup(request),
            ANDROID_WARMUP_START_FALLBACK_MS,
          );
        } catch {
          // 预热失败时恢复为原有直接播放路径，业务提示音仍有机会发出。
          playAudible(request);
        }
      })();
    },
    [
      ensureAudioMode,
      finishAndroidWarmup,
      isRequestCurrent,
      playAudible,
    ],
  );

  useEffect(() => {
    // 设置过程绝不阻塞首屏；设备或原生模块异常时静默退化为无音效。
    void ensureAudioMode();
  }, [ensureAudioMode]);

  useEffect(() => {
    mounted.current = true;
    const createdPlayers: AudioPlayer[] = [];
    const createdSubscriptions: ReturnType<AudioPlayer["addListener"]>[] = [];
    for (const cue of Object.keys(SOURCES) as PosSoundCue[]) {
      try {
        const player = createAudioPlayer(SOURCES[cue]);
        players.current[cue] = player;
        createdPlayers.push(player);

        try {
          createdSubscriptions.push(
            player.addListener(
              "playbackStatusUpdate",
              (status: AudioStatus) => {
                const request = activePlayback.current;
                if (!request) return;
                if (
                  request.warmupPlayer === player &&
                  status.playing &&
                  request.phase === "warming"
                ) {
                  if (!request.warmupObservedPlaying) {
                    request.warmupObservedPlaying = true;
                    clearWarmupTimer(request);
                    // 从原生音轨真正开始后等待完整预热窗口，避免首个短音落在功放唤醒期。
                    request.warmupTimer = setTimeout(
                      () => finishAndroidWarmup(request),
                      ANDROID_WARMUP_DELAY_MS,
                    );
                  }
                  return;
                }
                if (request.player !== player || request.cue !== cue) return;
                if (status.playing && request.phase === "audible") {
                  androidOutputWarmUntil.current =
                    Date.now() + ANDROID_OUTPUT_WARM_WINDOW_MS;
                  scanTiming.soundPlaying(cue);
                  return;
                }
                if (status.isLoaded && request.phase === "pending") {
                  startPlayback(request);
                }
              },
            ),
          );
        } catch {
          // 监听失败时仍保留该 player；后续通过 isLoaded 直接判断。
        }
      } catch {
        // 个别资源不可用时保留其他 cue，播放时会安全 no-op。
      }
    }

    return () => {
      // 使所有未完成的原生 seek/load 失效，防止卸载后异步回调反播。
      mounted.current = false;
      playbackSequence.current += 1;
      const playersToPause = new Set(createdPlayers);
      if (activePlayback.current) {
        clearWarmupTimer(activePlayback.current);
        stopAndroidWarmup(activePlayback.current);
        restoreFullVolume(activePlayback.current.player);
        playersToPause.add(activePlayback.current.player);
        scanTiming.discardExpectedSound(activePlayback.current.cue);
      }
      for (const player of playersToPause) {
        try {
          player.pause();
        } catch {
          // 卸载时单个原生播放器异常不能阻止其他资源释放。
        }
      }
      for (const subscription of createdSubscriptions) {
        try {
          subscription.remove();
        } catch {
          // 单个订阅解除失败不能影响 React 树退出。
        }
      }
      activePlayback.current = null;
      players.current = {};
      for (const player of createdPlayers) {
        try {
          // AudioPlayer 是 SharedObject，release 才能确定性解除原生资源。
          player.release();
        } catch {
          // 单个 SharedObject 释放失败不能影响 React 树退出。
        }
      }
    };
  }, [
    clearWarmupTimer,
    finishAndroidWarmup,
    restoreFullVolume,
    startPlayback,
    stopAndroidWarmup,
  ]);

  const requestPlayback = useCallback(
    (cue: PosSoundCue) => {
      const group = SOUND_GROUP_BY_CUE[cue];
      if (!enabledByGroup.current[group]) {
        scanTiming.discardExpectedSound(cue);
        return;
      }

      const player = players.current[cue];
      if (!player) {
        scanTiming.discardExpectedSound(cue);
        return;
      }
      const groupVersion = groupPlaybackVersion.current[group];
      const sequence = playbackSequence.current + 1;
      playbackSequence.current = sequence;

      const previousRequest = activePlayback.current;
      if (previousRequest) {
        clearWarmupTimer(previousRequest);
        stopAndroidWarmup(previousRequest);
        restoreFullVolume(previousRequest.player);
      }
      try {
        if (previousRequest) {
          previousRequest.player.pause();
        }
      } catch {
        // 先前音效的中断失败不应阻止当前 cue 尝试播放。
      }
      // 同 cue 的测量关联已由 expectSound 原子替换；跨 cue 覆盖则主动清理旧等待。
      if (previousRequest && previousRequest.cue !== cue) {
        scanTiming.discardExpectedSound(previousRequest.cue);
      }

      const request: ActivePlayback = {
        cue,
        group,
        groupVersion,
        phase: "pending",
        player,
        sequence,
        warmupObservedPlaying: false,
        warmupPlayer: null,
        warmupTimer: null,
      };
      activePlayback.current = request;

      // 目标 player 已加载时立即进入 seek+play；否则保持 pending，
      // load status 到来后再自动开始，避免冷启动首个音效被静默吞掉。
      if (isPlayerReadyToReplay(player)) {
        startPlayback(request);
      }
    },
    [
      clearWarmupTimer,
      restoreFullVolume,
      startPlayback,
      stopAndroidWarmup,
    ],
  );

  const setGroupEnabled = useCallback(
    (group: PosSoundGroup, nextEnabled: boolean) => {
      const changed = enabledByGroup.current[group] !== nextEnabled;
      enabledByGroup.current[group] = nextEnabled;

      if (group === "button") {
        setButtonSoundEnabledState(nextEnabled);
        void saveButtonSoundEnabled(nextEnabled);
      } else {
        setSpecialNodeSoundEnabledState(nextEnabled);
        void saveSpecialNodeSoundEnabled(nextEnabled);
      }

      if (!changed) return;
      groupPlaybackVersion.current[group] += 1;

      if (!nextEnabled) {
        const active = activePlayback.current;
        if (active?.group === group) {
          clearWarmupTimer(active);
          stopAndroidWarmup(active);
          restoreFullVolume(active.player);
          try {
            active.player.pause();
          } catch {
            // 原生暂停失败不能回滚设置；分组版本仍会阻止晚到的 seek 反播。
          }
          if (activePlayback.current === active) activePlayback.current = null;
          scanTiming.discardExpectedSound(active.cue);
        }
        return;
      }

      // 开启时明确试听本组代表音；关闭和同值设置均不发声。
      requestPlayback(group === "button" ? "tap" : "cart-added");
    },
    [
      clearWarmupTimer,
      requestPlayback,
      restoreFullVolume,
      stopAndroidWarmup,
    ],
  );

  const setButtonSoundEnabled = useCallback(
    (nextEnabled: boolean) => setGroupEnabled("button", nextEnabled),
    [setGroupEnabled],
  );

  const setSpecialNodeSoundEnabled = useCallback(
    (nextEnabled: boolean) => setGroupEnabled("special-node", nextEnabled),
    [setGroupEnabled],
  );

  const play = useCallback(
    (cue: PosSoundCue) => requestPlayback(cue),
    [requestPlayback],
  );

  const value = useMemo<PosSoundContextValue>(
    () => ({
      buttonSoundEnabled,
      play,
      setButtonSoundEnabled,
      setSpecialNodeSoundEnabled,
      specialNodeSoundEnabled,
    }),
    [
      buttonSoundEnabled,
      play,
      setButtonSoundEnabled,
      setSpecialNodeSoundEnabled,
      specialNodeSoundEnabled,
    ],
  );

  return (
    <PosSoundContext.Provider value={value}>{children}</PosSoundContext.Provider>
  );
}
