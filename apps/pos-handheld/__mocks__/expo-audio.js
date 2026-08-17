const { jest } = require("@jest/globals");

const mockAudioPlayers = [];

function createMockAudioPlayer() {
  const listeners = new Map();
  const player = {
    currentStatus: { isLoaded: true, playbackState: "ready" },
    isLoaded: true,
    volume: 1,
    pause: jest.fn(),
    play: jest.fn(),
    release: jest.fn(),
    remove: jest.fn(),
    replace: jest.fn(),
    seekTo: jest.fn().mockResolvedValue(undefined),
    addListener: jest.fn((eventName, listener) => {
      if (!listeners.has(eventName)) listeners.set(eventName, new Set());
      listeners.get(eventName).add(listener);
      return {
        remove: jest.fn(() => listeners.get(eventName)?.delete(listener)),
      };
    }),
    __emitStatus: jest.fn((status) => {
      for (const listener of listeners.get("playbackStatusUpdate") ?? []) {
        listener(status);
      }
    }),
  };
  mockAudioPlayers.push(player);
  return player;
}

const mockAudioPlayer = {
  currentStatus: { isLoaded: true, playbackState: "ready" },
  isLoaded: true,
  volume: 1,
  pause: jest.fn(),
  play: jest.fn(),
  release: jest.fn(),
  remove: jest.fn(),
  replace: jest.fn(),
  seekTo: jest.fn().mockResolvedValue(undefined),
  addListener: jest.fn(() => ({ remove: jest.fn() })),
  __emitStatus: jest.fn(),
};

function resetAudioMock() {
  for (const player of mockAudioPlayers) {
    player.currentStatus = { isLoaded: true, playbackState: "ready" };
    player.isLoaded = true;
    player.volume = 1;
    player.pause.mockReset();
    player.play.mockReset();
    player.release.mockReset();
    player.remove.mockReset();
    player.replace.mockReset();
    player.seekTo.mockReset();
    player.seekTo.mockResolvedValue(undefined);
    player.addListener.mockReset();
    player.__emitStatus.mockReset();
  }
  mockAudioPlayers.splice(0);
  mockAudioPlayer.currentStatus = { isLoaded: true, playbackState: "ready" };
  mockAudioPlayer.isLoaded = true;
  mockAudioPlayer.volume = 1;
  mockAudioPlayer.pause.mockReset();
  mockAudioPlayer.play.mockReset();
  mockAudioPlayer.release.mockReset();
  mockAudioPlayer.remove.mockReset();
  mockAudioPlayer.replace.mockReset();
  mockAudioPlayer.seekTo.mockReset();
  mockAudioPlayer.seekTo.mockResolvedValue(undefined);
  mockAudioPlayer.addListener.mockReset();
  mockAudioPlayer.addListener.mockImplementation(() => ({ remove: jest.fn() }));
  mockAudioPlayer.__emitStatus.mockReset();
  module.exports.setAudioModeAsync.mockReset();
  module.exports.setAudioModeAsync.mockResolvedValue(undefined);
  module.exports.createAudioPlayer.mockReset();
  module.exports.createAudioPlayer.mockImplementation(createMockAudioPlayer);
}

module.exports = {
  __mockAudioPlayer: mockAudioPlayer,
  __mockAudioPlayers: mockAudioPlayers,
  __resetAudioMock: resetAudioMock,
  createAudioPlayer: jest.fn(createMockAudioPlayer),
  setAudioModeAsync: jest.fn().mockResolvedValue(undefined),
  useAudioPlayer: jest.fn(() => mockAudioPlayer),
};
