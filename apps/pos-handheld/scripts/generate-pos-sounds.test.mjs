import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile, readdir, stat } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const appDirectory = resolve(scriptDirectory, "..");
const soundsDirectory = resolve(appDirectory, "assets/sounds");
const buttonCueNames = ["tap", "key", "navigate", "danger"];
const specialCueNames = [
  "query-found",
  "query-empty",
  "query-error",
  "cart-added",
  "cart-incremented",
  "cart-not-found",
  "cart-failed-blocked",
];
const cueNames = [...buttonCueNames, ...specialCueNames];
const specialPcmHashes = {
  "query-found": "ef4a93d299b178148ca5d2dbe7619b2aef2c2deff7fa4a45b410abaac34c3e9e",
  "query-empty": "b9d653d8b2a08988d0e1b4f04c7235005523356d989ea7dfc0f0e07a187eb522",
  "query-error": "e1fb4ee3a60f120a23b11b0afba187bd2e301e66c22c9cb14c01c1a314618af8",
  "cart-added": "1e347a5df26378d840bbf751d626a5c9a84bc58d262f36a60c9f7913cd75c0d0",
  "cart-incremented": "6f3d67e894d4155c305be1fba03d8a24c6a9b8005f5990244a3766e4cb85c343",
  "cart-not-found": "9282a6d5328ab85b9baab8516cfd3864d281a298426bdd9ab950ddfdfddef2d8",
  "cart-failed-blocked": "96d77556d628f2df5dc36bc2c7f296bd2d25a5e43d58d93d1ebabdc3e16cf1b4",
};

test("POS 音效资源保持 11 个原创单声道 PCM WAV，且生成脚本存在", async () => {
  await stat(resolve(scriptDirectory, "generate-pos-sounds.mjs"));
  const wavFiles = (await readdir(soundsDirectory))
    .filter((fileName) => fileName.endsWith(".wav"))
    .sort();
  assert.deepEqual(
    wavFiles,
    cueNames.map((cueName) => `${cueName}.wav`).sort(),
  );

  for (const cueName of cueNames) {
    const data = await readFile(resolve(soundsDirectory, `${cueName}.wav`));
    assert.equal(data.subarray(0, 4).toString("ascii"), "RIFF", cueName);
    assert.equal(data.subarray(8, 12).toString("ascii"), "WAVE", cueName);
    assert.equal(data.subarray(12, 16).toString("ascii"), "fmt ", cueName);
    assert.equal(data.readUInt32LE(16), 16, cueName);
    assert.equal(data.readUInt16LE(20), 1, cueName);
    assert.equal(data.readUInt16LE(22), 1, cueName);
    assert.equal(data.readUInt32LE(24), 22_050, cueName);
    assert.equal(data.readUInt32LE(28), 44_100, cueName);
    assert.equal(data.readUInt16LE(32), 2, cueName);
    assert.equal(data.readUInt16LE(34), 16, cueName);
    assert.equal(data.subarray(36, 40).toString("ascii"), "data", cueName);
    assert.equal(data.readUInt32LE(40), data.length - 44, cueName);
    assert.equal(data.readUInt32LE(4), data.length - 8, cueName);
    assert.ok(data.length > 44, cueName);
  }
});

test("四个普通按钮 cue 使用同一份约 30ms 的轻量点击 PCM", async () => {
  const payloads = await Promise.all(
    buttonCueNames.map(async (cueName) => {
      const data = await readFile(resolve(soundsDirectory, `${cueName}.wav`));
      return data.subarray(44);
    }),
  );

  for (const payload of payloads.slice(1)) {
    assert.deepEqual(payload, payloads[0]);
  }

  const sampleCount = payloads[0].length / 2;
  assert.ok(sampleCount >= 640 && sampleCount <= 684, sampleCount);

  let peak = 0;
  for (let offset = 0; offset < payloads[0].length; offset += 2) {
    peak = Math.max(peak, Math.abs(payloads[0].readInt16LE(offset)));
  }
  assert.ok(peak >= 0.14 * 32_767 && peak <= 0.17 * 32_767, peak);
});

test("七个特殊节点 cue 彼此独立，且不复用普通按钮点击 PCM", async () => {
  const buttonPcm = (
    await readFile(resolve(soundsDirectory, `${buttonCueNames[0]}.wav`))
  )
    .subarray(44)
    .toString("base64");
  const specialPayloads = new Set();

  for (const cueName of specialCueNames) {
    const pcmData = (
      await readFile(resolve(soundsDirectory, `${cueName}.wav`))
    ).subarray(44);
    const pcmBase64 = pcmData.toString("base64");
    const pcmHash = createHash("sha256").update(pcmData).digest("hex");
    assert.equal(pcmHash, specialPcmHashes[cueName], cueName);
    assert.notEqual(pcmBase64, buttonPcm, cueName);
    assert.equal(
      specialPayloads.has(pcmBase64),
      false,
      `${cueName} 的 PCM 数据与其他特殊 cue 重复`,
    );
    specialPayloads.add(pcmBase64);
  }

  assert.equal(specialPayloads.size, specialCueNames.length);
});
