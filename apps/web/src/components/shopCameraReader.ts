export type ShopCameraFailureReason =
  | 'insecure'
  | 'permission_denied'
  | 'unavailable'
  | 'in_use'
  | 'unknown'

export interface ShopCameraReaderControls {
  pause: () => void
  resume: () => void
  stop: () => void
}

interface ShopCameraZxingResult {
  getText: () => string
}

interface ShopCameraZxingControls {
  stop: () => void
}

interface ShopCameraZxingReader {
  decodeFromStream: (
    stream: MediaStream,
    video: HTMLVideoElement,
    callback: (
      result?: ShopCameraZxingResult,
      error?: unknown,
      controls?: ShopCameraZxingControls,
    ) => void,
  ) => Promise<ShopCameraZxingControls>
}

interface ShopCameraZxingModules {
  BarcodeFormat: {
    CODE_128: unknown
    EAN_13: unknown
  }
  BrowserMultiFormatReader: new (
    hints?: Map<unknown, unknown>,
    options?: { delayBetweenScanAttempts?: number },
  ) => ShopCameraZxingReader
  DecodeHintType: {
    POSSIBLE_FORMATS: unknown
  }
}

export type ShopCameraZxingLoader = () => Promise<ShopCameraZxingModules>

export interface StartShopCameraReaderOptions {
  getUserMedia?: (constraints: MediaStreamConstraints) => Promise<MediaStream>
  loadModules?: ShopCameraZxingLoader
  onError?: (error: unknown) => void
  onResult: (barcode: string) => void
  onSighting?: (barcode: string) => void
  signal?: AbortSignal
  video: HTMLVideoElement
}

const ROUTINE_DECODE_ERROR_NAMES = new Set([
  'ChecksumException',
  'FormatException',
  'NotFoundException',
])

function getCameraErrorName(error: unknown) {
  if (typeof error !== 'object' || error === null) {
    return ''
  }

  const errorLike = error as {
    getKind?: () => unknown
    name?: unknown
  }

  // ZXing 生产压缩后 error.name 会被改名，getKind() 仍保留稳定的解码异常类型。
  if (typeof errorLike.getKind === 'function') {
    try {
      const kind = errorLike.getKind()
      if (typeof kind === 'string' && kind) {
        return kind
      }
    } catch {
      // 非 ZXing 错误对象的方法异常不得阻断标准 DOMException.name 回退。
    }
  }

  return 'name' in error ? String(errorLike.name) : ''
}

function createShopCameraAbortError() {
  const error = new Error('Camera start aborted')
  error.name = 'AbortError'
  return error
}

async function loadShopCameraZxingModules(): Promise<ShopCameraZxingModules> {
  const [browserModule, libraryModule] = await Promise.all([
    import('@zxing/browser'),
    import('@zxing/library'),
  ])

  return {
    BarcodeFormat: libraryModule.BarcodeFormat,
    BrowserMultiFormatReader: browserModule.BrowserMultiFormatReader,
    DecodeHintType: libraryModule.DecodeHintType,
  } as unknown as ShopCameraZxingModules
}

export function classifyShopCameraFailure(error: unknown, secureContext: boolean): ShopCameraFailureReason {
  if (!secureContext) {
    return 'insecure'
  }

  const name = getCameraErrorName(error)

  if (name === 'NotAllowedError' || name === 'SecurityError') {
    return 'permission_denied'
  }
  if (name === 'NotFoundError' || name === 'OverconstrainedError') {
    return 'unavailable'
  }
  if (name === 'NotReadableError') {
    return 'in_use'
  }

  return 'unknown'
}

export async function startShopCameraReader(
  options: StartShopCameraReaderOptions,
): Promise<ShopCameraReaderControls> {
  let stopped = false
  let paused = false
  let ownedStream: MediaStream | null = null
  let runtimeFailed = false
  let zxingControls: ShopCameraZxingControls | undefined
  const stoppedControls = new WeakSet<ShopCameraZxingControls>()
  const stoppedTracks = new WeakSet<MediaStreamTrack>()

  const releaseVideo = () => {
    ownedStream?.getTracks?.().forEach((track) => {
      if (stoppedTracks.has(track) || track.readyState === 'ended') {
        return
      }
      stoppedTracks.add(track)
      try {
        track.stop()
      } catch {
        // 某一轨道停止失败时仍需继续释放其余轨道并解除 video 绑定。
      }
    })
    // 旧会话迟到清理时，video 可能已由新会话复用；只能解除本会话自己的流。
    if (ownedStream && options.video.srcObject === ownedStream) {
      options.video.srcObject = null
    }
  }

  const stopReader = (controls = zxingControls) => {
    const replacementSource = ownedStream && options.video.srcObject !== ownedStream
      ? options.video.srcObject
      : null
    try {
      if (controls && !stoppedControls.has(controls)) {
        stoppedControls.add(controls)
        controls.stop()
      }
    } catch {
      // ZXing 停止异常不得阻断浏览器媒体流释放。
    } finally {
      releaseVideo()
      if (replacementSource && options.video.srcObject !== replacementSource) {
        options.video.srcObject = replacementSource
        void options.video.play().catch(() => {
          // 新会话会继续接管预览；恢复播放失败不应覆盖其相机状态。
        })
      }
    }
  }

  const handleAbort = () => {
    if (stopped) {
      return
    }

    stopped = true
    options.signal?.removeEventListener('abort', handleAbort)
    stopReader()
  }

  options.signal?.addEventListener('abort', handleAbort, { once: true })
  if (options.signal?.aborted) {
    handleAbort()
  }

  try {
    if (stopped) {
      throw createShopCameraAbortError()
    }

    const {
      BarcodeFormat,
      BrowserMultiFormatReader,
      DecodeHintType,
    } = await (options.loadModules ?? loadShopCameraZxingModules)()
    if (stopped) {
      throw createShopCameraAbortError()
    }

    const hints = new Map<unknown, unknown>([
      [DecodeHintType.POSSIBLE_FORMATS, [BarcodeFormat.EAN_13, BarcodeFormat.CODE_128]],
    ])
    const reader = new BrowserMultiFormatReader(hints, {
      delayBetweenScanAttempts: 100,
    })

    const constraints: MediaStreamConstraints = {
      audio: false,
      video: {
        facingMode: { ideal: 'environment' },
        height: { ideal: 720 },
        width: { ideal: 1280 },
      },
    }
    const getUserMedia = options.getUserMedia
      ?? ((nextConstraints: MediaStreamConstraints) => navigator.mediaDevices.getUserMedia(nextConstraints))
    const stream = await getUserMedia(constraints)
    ownedStream = stream
    if (stopped) {
      releaseVideo()
      throw createShopCameraAbortError()
    }

    zxingControls = await reader.decodeFromStream(
      stream,
      options.video,
      (result, error) => {
        if (stopped) {
          return
        }
        const barcode = result?.getText().trim()
        if (barcode) {
          if (paused) {
            options.onSighting?.(barcode)
            return
          }
          options.onResult(barcode)
          return
        }

        if (error && !ROUTINE_DECODE_ERROR_NAMES.has(getCameraErrorName(error))) {
          stopped = true
          runtimeFailed = true
          options.signal?.removeEventListener('abort', handleAbort)
          options.onError?.(error)
          // ZXing 会在回调返回后同步 finalize；微任务兜底仅释放未被其清理的流。
          queueMicrotask(releaseVideo)
        }
      },
    )

    if (stopped) {
      if (!runtimeFailed) {
        stopReader()
      } else {
        releaseVideo()
      }
      throw createShopCameraAbortError()
    }
  } catch (error) {
    // ZXing 初始化失败时也可能已经取得媒体流，必须在向上抛出前主动释放。
    options.signal?.removeEventListener('abort', handleAbort)
    if (!stopped) {
      stopped = true
      stopReader()
    }
    throw error
  }

  return {
    pause: () => {
      paused = true
    },
    resume: () => {
      if (!stopped) {
        paused = false
      }
    },
    stop: () => {
      if (stopped) {
        return
      }
      stopped = true
      options.signal?.removeEventListener('abort', handleAbort)
      stopReader()
    },
  }
}
