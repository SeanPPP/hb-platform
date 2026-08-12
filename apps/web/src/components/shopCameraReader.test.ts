import assert from 'node:assert/strict'
import {
  classifyShopCameraFailure,
  startShopCameraReader,
  type ShopCameraZxingLoader,
} from './shopCameraReader'

const possibleFormatsToken = Symbol('possible-formats')
const ean13Token = Symbol('ean13')
const code128Token = Symbol('code128')
let capturedHints: Map<unknown, unknown> | undefined
let capturedReaderOptions: { delayBetweenScanAttempts?: number } | undefined
let capturedConstraints: MediaStreamConstraints | undefined
let capturedStream: MediaStream | undefined
let capturedVideo: HTMLVideoElement | undefined
let capturedCallback:
  | ((result?: { getText: () => string }, error?: unknown, controls?: { stop: () => void }) => void)
  | undefined
let underlyingStopCount = 0
let trackStopCount = 0
let loadModulesCount = 0

const underlyingControls = {
  stop() {
    underlyingStopCount += 1
  },
}

class FakeReader {
  constructor(hints?: Map<unknown, unknown>, options?: { delayBetweenScanAttempts?: number }) {
    capturedHints = hints
    capturedReaderOptions = options
  }

  async decodeFromStream(
    stream: MediaStream,
    video: HTMLVideoElement,
    callback: (result?: { getText: () => string }, error?: unknown, controls?: { stop: () => void }) => void,
  ) {
    capturedStream = stream
    capturedVideo = video
    video.srcObject = stream
    capturedCallback = callback
    return underlyingControls
  }
}

const loadModules: ShopCameraZxingLoader = async () => {
  loadModulesCount += 1
  return {
    BarcodeFormat: {
      CODE_128: code128Token,
      EAN_13: ean13Token,
    },
    BrowserMultiFormatReader: FakeReader,
    DecodeHintType: {
      POSSIBLE_FORMATS: possibleFormatsToken,
    },
  }
}

const mediaStream = {
  getTracks: () => [{ stop: () => { trackStopCount += 1 } }],
} as unknown as MediaStream
const video = { srcObject: null } as unknown as HTMLVideoElement
const results: string[] = []
const sightings: string[] = []

assert.equal(loadModulesCount, 0, '未开启相机前不得加载 ZXing')
const controls = await startShopCameraReader({
  getUserMedia: async (constraints) => {
    capturedConstraints = constraints
    return mediaStream
  },
  loadModules,
  onResult: (barcode) => results.push(barcode),
  onSighting: (barcode) => sightings.push(barcode),
  video,
})
assert.equal(loadModulesCount, 1, '开启相机时才应加载 ZXing')

assert.deepEqual(
  capturedHints?.get(possibleFormatsToken),
  [ean13Token, code128Token],
  '解码器必须限定 EAN-13 与 Code 128',
)
assert.equal(capturedReaderOptions?.delayBetweenScanAttempts, 100, '连续解码应限制帧间尝试频率')
assert.equal(capturedStream, mediaStream, '取得媒体流后才应交给 ZXing 持续解码')
assert.deepEqual(
  capturedConstraints,
  {
    audio: false,
    video: {
      facingMode: { ideal: 'environment' },
      height: { ideal: 720 },
      width: { ideal: 1280 },
    },
  },
  '应优先使用 720p 后置摄像头且不得申请麦克风',
)
assert.equal(capturedVideo, video, 'ZXing 应绑定传入的视频元素')

capturedCallback?.({ getText: () => ' 930000000001\r\n' })
capturedCallback?.({ getText: () => '   ' })
assert.deepEqual(results, ['930000000001'], '识别值应去除首尾空白并忽略空结果')

controls.pause()
capturedCallback?.({ getText: () => '930000000002' })
assert.deepEqual(results, ['930000000001'], '暂停期间不得继续派发识别结果')
assert.deepEqual(sightings, ['930000000002'], '暂停期间仍应刷新同码在画面中的目击时间')
controls.resume()
capturedCallback?.({ getText: () => '930000000003' })
assert.deepEqual(results, ['930000000001', '930000000003'], '恢复后应继续派发识别结果')

controls.stop()
controls.stop()
assert.equal(underlyingStopCount, 1, '停止解码必须幂等')
assert.equal(trackStopCount, 1, '停止时必须释放所有视频轨道')
assert.equal(video.srcObject, null, '停止后必须解除 video 与媒体流的绑定')

assert.equal(classifyShopCameraFailure({}, false), 'insecure', '非安全上下文应给出独立提示')
assert.equal(classifyShopCameraFailure({ name: 'NotAllowedError' }, true), 'permission_denied')
assert.equal(classifyShopCameraFailure({ name: 'SecurityError' }, true), 'permission_denied')
assert.equal(classifyShopCameraFailure({ name: 'NotFoundError' }, true), 'unavailable')
assert.equal(classifyShopCameraFailure({ name: 'OverconstrainedError' }, true), 'unavailable')
assert.equal(classifyShopCameraFailure({ name: 'NotReadableError' }, true), 'in_use')
assert.equal(classifyShopCameraFailure({ name: 'AbortError' }, true), 'unknown')
assert.equal(classifyShopCameraFailure(new Error('unexpected'), true), 'unknown')

let runtimeStopCount = 0
let runtimeTrackStopCount = 0
let runtimeCallback:
  | ((result?: { getText: () => string }, error?: unknown, controls?: { stop: () => void }) => void)
  | undefined
const runtimeStream = {
  getTracks: () => [{ stop: () => { runtimeTrackStopCount += 1 } }],
} as unknown as MediaStream
const runtimeVideo = { srcObject: runtimeStream } as unknown as HTMLVideoElement
const runtimeErrors: unknown[] = []
const runtimeControls = {
  stop() {
    runtimeStopCount += 1
  },
}

class RuntimeErrorReader {
  async decodeFromStream(
    stream: MediaStream,
    targetVideo: HTMLVideoElement,
    callback: (result?: { getText: () => string }, error?: unknown, controls?: { stop: () => void }) => void,
  ) {
    targetVideo.srcObject = stream
    runtimeCallback = callback
    return runtimeControls
  }
}

await startShopCameraReader({
  getUserMedia: async () => runtimeStream,
  loadModules: async () => ({
    BarcodeFormat: { CODE_128: code128Token, EAN_13: ean13Token },
    BrowserMultiFormatReader: RuntimeErrorReader,
    DecodeHintType: { POSSIBLE_FORMATS: possibleFormatsToken },
  }),
  onError: (error) => runtimeErrors.push(error),
  onResult: () => undefined,
  video: runtimeVideo,
})

runtimeCallback?.(undefined, { name: 'NotFoundException' }, runtimeControls)
assert.equal(runtimeErrors.length, 0, '正常的未识别帧不得误报为摄像头故障')
assert.equal(runtimeTrackStopCount, 0, '正常的未识别帧不得停止视频流')

runtimeCallback?.(undefined, {
  getKind: () => 'NotFoundException',
  name: 'e',
}, runtimeControls)
assert.equal(runtimeErrors.length, 0, '生产压缩改名后仍应识别 ZXing 的例行未找到异常')
assert.equal(runtimeTrackStopCount, 0, '生产压缩改名后的例行异常不得停止视频流')

const runtimeFailure = { name: 'NotReadableError' }
runtimeCallback?.(undefined, runtimeFailure, runtimeControls)
await Promise.resolve()
assert.deepEqual(runtimeErrors, [runtimeFailure], '运行期摄像头故障应通知调用方')
assert.equal(runtimeStopCount, 0, '非例行错误由 ZXing 自身 finalize，不应重复调用 controls.stop')
assert.equal(runtimeTrackStopCount, 1, '运行期摄像头故障应释放视频轨道')
assert.equal(runtimeVideo.srcObject, null, '运行期摄像头故障应解除 video 与媒体流的绑定')

let resolvePendingStart: ((controls: { stop: () => void }) => void) | undefined
let markPendingStartReady: (() => void) | undefined
let pendingStartTrackStopCount = 0
let pendingStartControlStopCount = 0
let pendingReplacementTrackStopCount = 0
let pendingReplacementPlayCount = 0
const pendingStartReady = new Promise<void>((resolve) => {
  markPendingStartReady = resolve
})
const pendingStartStream = {
  getTracks: () => [{ stop: () => { pendingStartTrackStopCount += 1 } }],
} as unknown as MediaStream
const pendingReplacementStream = {
  getTracks: () => [{ stop: () => { pendingReplacementTrackStopCount += 1 } }],
} as unknown as MediaStream
const pendingStartVideo = {
  srcObject: null,
  play: async () => { pendingReplacementPlayCount += 1 },
} as unknown as HTMLVideoElement
const pendingStartControls = {
  stop() {
    pendingStartControlStopCount += 1
    // 模拟 ZXing controls.stop() 的 finalize：它会清空当时 video 上的来源。
    pendingStartVideo.srcObject = null
  },
}

class PendingStartReader {
  async decodeFromStream(
    stream: MediaStream,
    targetVideo: HTMLVideoElement,
  ): Promise<{ stop: () => void }> {
    targetVideo.srcObject = stream
    markPendingStartReady?.()
    return new Promise((resolve) => {
      resolvePendingStart = resolve
    })
  }
}

const pendingStartAbortController = new AbortController()
const pendingStartPromise = startShopCameraReader({
  getUserMedia: async () => pendingStartStream,
  loadModules: async () => ({
    BarcodeFormat: { CODE_128: code128Token, EAN_13: ean13Token },
    BrowserMultiFormatReader: PendingStartReader,
    DecodeHintType: { POSSIBLE_FORMATS: possibleFormatsToken },
  }),
  onResult: () => undefined,
  signal: pendingStartAbortController.signal,
  video: pendingStartVideo,
})
await pendingStartReady
pendingStartAbortController.abort()
assert.equal(pendingStartTrackStopCount, 1, '启动尚未完成时中止也应立即释放已绑定视频轨道')
assert.equal(pendingStartVideo.srcObject, null, '启动中止后应立即解除 video 绑定')
pendingStartVideo.srcObject = pendingReplacementStream
resolvePendingStart?.(pendingStartControls)
await assert.rejects(pendingStartPromise, { name: 'AbortError' }, '迟到的启动结果不得恢复已中止会话')
assert.equal(pendingStartControlStopCount, 1, '迟到返回的 ZXing controls 也必须停止')
assert.equal(pendingReplacementTrackStopCount, 0, '旧 controls 停止不得释放新会话轨道')
assert.equal(pendingStartVideo.srcObject, pendingReplacementStream, '旧 controls 清理后应恢复新会话 video 绑定')
assert.equal(pendingReplacementPlayCount, 1, '恢复新会话流后应重新触发静音预览播放')

let resolveLateStream: ((stream: MediaStream) => void) | undefined
let markLateMediaRequested: (() => void) | undefined
let lateStreamTrackStopCount = 0
let lateDecodeCallCount = 0
const lateMediaRequested = new Promise<void>((resolve) => {
  markLateMediaRequested = resolve
})
const lateStream = {
  getTracks: () => [{ stop: () => { lateStreamTrackStopCount += 1 } }],
} as unknown as MediaStream
const lateStreamVideo = { srcObject: null } as unknown as HTMLVideoElement
let replacementTrackStopCount = 0
const replacementStream = {
  getTracks: () => [{ stop: () => { replacementTrackStopCount += 1 } }],
} as unknown as MediaStream

class LateStreamReader {
  async decodeFromStream(): Promise<{ stop: () => void }> {
    lateDecodeCallCount += 1
    return { stop: () => undefined }
  }
}

const lateStreamAbortController = new AbortController()
const lateStreamStartPromise = startShopCameraReader({
  getUserMedia: async () => {
    markLateMediaRequested?.()
    return new Promise((resolve) => {
      resolveLateStream = resolve
    })
  },
  loadModules: async () => ({
    BarcodeFormat: { CODE_128: code128Token, EAN_13: ean13Token },
    BrowserMultiFormatReader: LateStreamReader,
    DecodeHintType: { POSSIBLE_FORMATS: possibleFormatsToken },
  }),
  onResult: () => undefined,
  signal: lateStreamAbortController.signal,
  video: lateStreamVideo,
})
await lateMediaRequested
lateStreamAbortController.abort()
lateStreamVideo.srcObject = replacementStream
resolveLateStream?.(lateStream)
await assert.rejects(lateStreamStartPromise, { name: 'AbortError' }, '权限结果迟到时不得恢复已中止会话')
assert.equal(lateDecodeCallCount, 0, '已中止会话的迟到媒体流不得交给 ZXing 或绑定 video')
assert.equal(lateStreamTrackStopCount, 1, '迟到媒体流的每条轨道必须立即停止')
assert.equal(replacementTrackStopCount, 0, '旧会话清理不得停止已绑定到 video 的新会话流')
assert.equal(lateStreamVideo.srcObject, replacementStream, '旧会话清理不得清空新会话的 video 绑定')

let failedStartTrackStopCount = 0
const failedStartStream = {
  getTracks: () => [{ stop: () => { failedStartTrackStopCount += 1 } }],
} as unknown as MediaStream
const failedStartVideo = { srcObject: null } as unknown as HTMLVideoElement

class FailingReader {
  async decodeFromStream(
    stream: MediaStream,
    targetVideo: HTMLVideoElement,
  ): Promise<{ stop: () => void }> {
    targetVideo.srcObject = stream
    throw Object.assign(new Error('camera failed'), { name: 'NotReadableError' })
  }
}

await assert.rejects(
  startShopCameraReader({
    getUserMedia: async () => failedStartStream,
    loadModules: async () => ({
      BarcodeFormat: { CODE_128: code128Token, EAN_13: ean13Token },
      BrowserMultiFormatReader: FailingReader,
      DecodeHintType: { POSSIBLE_FORMATS: possibleFormatsToken },
    }),
    onResult: () => undefined,
    video: failedStartVideo,
  }),
  { name: 'NotReadableError' },
  '初始化失败应向调用方保留可分类的错误类型',
)
assert.equal(failedStartTrackStopCount, 1, '初始化失败也必须释放已取得的视频轨道')
assert.equal(failedStartVideo.srcObject, null, '初始化失败后必须解除 video 与媒体流的绑定')

console.log('shopCameraReader.test.ts: ok')
