import { Buffer } from "node:buffer";
import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SAMPLE_RATE = 22_050;
const OUTPUT_DIRECTORY = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "../assets/sounds",
);
const BUTTON_CLICK_DURATION = 0.03;
const buttonCueNames = ["tap", "key", "navigate", "danger"];

/**
 * 所有声音均由本脚本生成：短促、单声道、无语音且不依赖第三方采样。
 * 特殊节点 cue 保留各自的频率、时长和泛音比例，方便在嘈杂门店中区分。
 */
const specialCues = {
  "query-found": { duration: 0.09, frequency: 880, harmonic: 0.14 },
  "query-empty": { duration: 0.08, frequency: 470, harmonic: 0.1 },
  "query-error": { duration: 0.12, frequency: 290, harmonic: 0.24 },
  "cart-added": { duration: 0.1, frequency: 790, harmonic: 0.16 },
  "cart-incremented": { duration: 0.07, frequency: 830, harmonic: 0.12 },
  "cart-not-found": { duration: 0.1, frequency: 380, harmonic: 0.16 },
  "cart-failed-blocked": { duration: 0.13, frequency: 250, harmonic: 0.22 },
};

function renderPcm({ duration, frequency, harmonic }) {
  const sampleCount = Math.round(duration * SAMPLE_RATE);
  const pcm = Buffer.alloc(sampleCount * 2);

  for (let index = 0; index < sampleCount; index += 1) {
    const progress = index / sampleCount;
    const fadeIn = Math.min(1, progress * 40);
    const fadeOut = Math.min(1, (1 - progress) * 18);
    const envelope = fadeIn * fadeOut;
    const phase = (2 * Math.PI * frequency * index) / SAMPLE_RATE;
    const sample =
      (Math.sin(phase) + harmonic * Math.sin(phase * 2)) * envelope * 0.28;
    pcm.writeInt16LE(Math.round(sample * 32_767), index * 2);
  }

  return pcm;
}

/**
 * 普通按钮统一使用原创轻点击声：双频瞬态、快速起音、指数衰减和短尾淡出。
 * 浮点波形先归一化至约 0.16 full scale，确保每次生成结果确定且音量稳定。
 */
function renderButtonClick() {
  const sampleCount = Math.round(BUTTON_CLICK_DURATION * SAMPLE_RATE);
  const samples = new Float64Array(sampleCount);
  let rawPeak = 0;

  for (let index = 0; index < sampleCount; index += 1) {
    const time = index / SAMPLE_RATE;
    const attack = Math.min(1, time / 0.001);
    const decay = Math.exp(-time / 0.008);
    const tailFade = Math.min(
      1,
      Math.max(0, (BUTTON_CLICK_DURATION - time) / 0.003),
    );
    const wave =
      0.75 * Math.sin(2 * Math.PI * 1_300 * time) +
      0.25 * Math.sin(2 * Math.PI * 2_100 * time);
    const sample = wave * attack * decay * tailFade;
    samples[index] = sample;
    rawPeak = Math.max(rawPeak, Math.abs(sample));
  }

  const pcm = Buffer.alloc(sampleCount * 2);
  const scale = 0.16 / rawPeak;
  for (let index = 0; index < sampleCount; index += 1) {
    pcm.writeInt16LE(Math.round(samples[index] * scale * 32_767), index * 2);
  }

  return pcm;
}

function wavFile(pcm) {
  const header = Buffer.alloc(44);
  header.write("RIFF", 0, "ascii");
  header.writeUInt32LE(36 + pcm.length, 4);
  header.write("WAVE", 8, "ascii");
  header.write("fmt ", 12, "ascii");
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(1, 22);
  header.writeUInt32LE(SAMPLE_RATE, 24);
  header.writeUInt32LE(SAMPLE_RATE * 2, 28);
  header.writeUInt16LE(2, 32);
  header.writeUInt16LE(16, 34);
  header.write("data", 36, "ascii");
  header.writeUInt32LE(pcm.length, 40);
  return Buffer.concat([header, pcm]);
}

await mkdir(OUTPUT_DIRECTORY, { recursive: true });
const buttonClickPcm = renderButtonClick();
await Promise.all(
  [
    ...buttonCueNames.map((cueName) => [cueName, buttonClickPcm]),
    ...Object.entries(specialCues).map(([cueName, cue]) => [
      cueName,
      renderPcm(cue),
    ]),
  ].map(async ([cueName, pcm]) => {
    await writeFile(resolve(OUTPUT_DIRECTORY, `${cueName}.wav`), wavFile(pcm));
  }),
);
