const { jest } = require("@jest/globals");

const mockAudioPlayers = [];

function createMockAudioPlayer() {
  const player = {
    pause: jest.fn(),
    play: jest.fn(),
    release: jest.fn(),
    remove: jest.fn(),
    replace: jest.fn(),
    seekTo: jest.fn().mockResolvedValue(undefined),
  };
  mockAudioPlayers.push(player);
  return player;
}

const mockAudioPlayer = {
  pause: jest.fn(),
  play: jest.fn(),
  release: jest.fn(),
  remove: jest.fn(),
  replace: jest.fn(),
  seekTo: jest.fn().mockResolvedValue(undefined),
};

function resetAudioMock() {
  for (const player of mockAudioPlayers) {
    player.pause.mockReset();
    player.play.mockReset();
    player.release.mockReset();
    player.remove.mockReset();
    player.replace.mockReset();
    player.seekTo.mockReset();
    player.seekTo.mockResolvedValue(undefined);
  }
  mockAudioPlayers.splice(0);
  mockAudioPlayer.pause.mockReset();
  mockAudioPlayer.play.mockReset();
  mockAudioPlayer.release.mockReset();
  mockAudioPlayer.remove.mockReset();
  mockAudioPlayer.replace.mockReset();
  mockAudioPlayer.seekTo.mockReset();
  mockAudioPlayer.seekTo.mockResolvedValue(undefined);
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
