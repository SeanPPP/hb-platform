import { ReloadOutlined } from '@ant-design/icons'
import { Button, Spin, Tag, Typography } from 'antd'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  createShopCameraScanQueue,
  SHOP_CAMERA_SCAN_MAX_QUEUE,
  type ShopCameraScanQueueSnapshot,
} from './shopCameraScanQueue'
import {
  classifyShopCameraFailure,
  startShopCameraReader,
  type ShopCameraFailureReason,
  type ShopCameraReaderControls,
} from './shopCameraReader'

const { Text } = Typography

export type ShopCameraSubmitOutcome =
  | 'added'
  | 'multiple'
  | 'not_found'
  | 'blocked'
  | 'error'
  | 'ignored'

interface ShopCameraScannerProps {
  paused: boolean
  sessionKey: string
  onRequestClose: () => void
  onSubmit: (barcode: string) => Promise<ShopCameraSubmitOutcome>
}

type ShopCameraPhase = 'starting' | 'scanning' | 'processing' | 'waiting'

const EMPTY_QUEUE_SNAPSHOT: ShopCameraScanQueueSnapshot = {
  pendingCount: 0,
}

export default function ShopCameraScanner({
  paused,
  sessionKey,
  onRequestClose,
  onSubmit,
}: ShopCameraScannerProps) {
  const { t } = useTranslation()
  const videoRef = useRef<HTMLVideoElement>(null)
  const readerControlsRef = useRef<ShopCameraReaderControls | null>(null)
  const queueRef = useRef(createShopCameraScanQueue())
  const onSubmitRef = useRef(onSubmit)
  const pausedRef = useRef(paused)
  const processingRef = useRef(false)
  const waitingForPickerRef = useRef(false)
  const pickerPauseSeenRef = useRef(false)
  const sessionGenerationRef = useRef(0)
  const readerReadyRef = useRef(false)
  const [phase, setPhase] = useState<ShopCameraPhase>('starting')
  const [failureReason, setFailureReason] = useState<ShopCameraFailureReason | null>(null)
  const [queueSnapshot, setQueueSnapshot] = useState<ShopCameraScanQueueSnapshot>(EMPTY_QUEUE_SNAPSHOT)
  const [queueFull, setQueueFull] = useState(false)
  const [retryVersion, setRetryVersion] = useState(0)

  onSubmitRef.current = onSubmit

  const refreshQueueSnapshot = useCallback(() => {
    const nextSnapshot = queueRef.current.getSnapshot()
    setQueueSnapshot(nextSnapshot)
    const retainedCount = nextSnapshot.pendingCount + (nextSnapshot.processingValue ? 1 : 0)
    if (retainedCount < SHOP_CAMERA_SCAN_MAX_QUEUE) {
      setQueueFull(false)
    }
  }, [])

  const drainQueue = useCallback(async function consumeQueue() {
    if (processingRef.current || pausedRef.current || waitingForPickerRef.current) {
      return
    }

    const lease = queueRef.current.takeNext()
    refreshQueueSnapshot()
    if (!lease) {
      if (readerReadyRef.current) {
        setPhase('scanning')
      }
      return
    }

    const generation = sessionGenerationRef.current
    processingRef.current = true
    setPhase('processing')

    let outcome: ShopCameraSubmitOutcome = 'error'
    try {
      outcome = await onSubmitRef.current(lease.value)
    } catch {
      outcome = 'error'
    }

    if (generation !== sessionGenerationRef.current) {
      return
    }

    queueRef.current.finish(lease)
    processingRef.current = false
    refreshQueueSnapshot()

    if (outcome === 'multiple') {
      // React 提交弹窗状态前先同步关住入口，避免这一个渲染间隙继续收码。
      waitingForPickerRef.current = true
      pickerPauseSeenRef.current = pausedRef.current
      queueRef.current.setPaused(true)
      readerControlsRef.current?.pause()
      setPhase('waiting')
      return
    }

    if (pausedRef.current) {
      setPhase('waiting')
      return
    }

    setPhase('scanning')
    queueMicrotask(() => {
      void consumeQueue()
    })
  }, [refreshQueueSnapshot])

  useEffect(() => {
    pausedRef.current = paused
    queueRef.current.setPaused(paused)

    if (paused) {
      readerControlsRef.current?.pause()
      if (waitingForPickerRef.current) {
        pickerPauseSeenRef.current = true
      }
      setPhase('waiting')
      return
    }

    readerControlsRef.current?.resume()
    if (waitingForPickerRef.current && pickerPauseSeenRef.current) {
      waitingForPickerRef.current = false
      pickerPauseSeenRef.current = false
    }
    void drainQueue()
  }, [drainQueue, paused])

  useEffect(() => {
    const generation = sessionGenerationRef.current + 1
    sessionGenerationRef.current = generation
    readerReadyRef.current = false
    processingRef.current = false
    waitingForPickerRef.current = false
    pickerPauseSeenRef.current = false
    queueRef.current.reset()
    queueRef.current.setPaused(pausedRef.current)
    setQueueSnapshot(EMPTY_QUEUE_SNAPSHOT)
    setQueueFull(false)
    setFailureReason(null)
    setPhase('starting')

    const video = videoRef.current
    if (!video) {
      setFailureReason('unknown')
      return undefined
    }

    if (!window.isSecureContext) {
      setFailureReason('insecure')
      return undefined
    }

    if (!navigator.mediaDevices?.getUserMedia) {
      setFailureReason('unavailable')
      return undefined
    }

    let disposed = false
    let runtimeFailed = false
    const abortController = new AbortController()

    void startShopCameraReader({
      video,
      onError: (error) => {
        runtimeFailed = true
        if (disposed || generation !== sessionGenerationRef.current) {
          return
        }

        readerReadyRef.current = false
        setFailureReason(classifyShopCameraFailure(error, window.isSecureContext))
      },
      onResult: (barcode) => {
        if (disposed || generation !== sessionGenerationRef.current) {
          return
        }

        const enqueueResult = queueRef.current.enqueue(barcode, Date.now())
        if (enqueueResult === 'full') {
          setQueueFull(true)
          return
        }
        if (enqueueResult !== 'queued') {
          return
        }

        refreshQueueSnapshot()
        void drainQueue()
      },
      onSighting: (barcode) => {
        if (!disposed && generation === sessionGenerationRef.current) {
          queueRef.current.noteSighting(barcode, Date.now())
        }
      },
      signal: abortController.signal,
    }).then((controls) => {
      if (disposed || runtimeFailed || generation !== sessionGenerationRef.current) {
        controls.stop()
        return
      }

      readerControlsRef.current = controls
      readerReadyRef.current = true
      if (pausedRef.current || waitingForPickerRef.current) {
        controls.pause()
        setPhase('waiting')
      } else if (!processingRef.current) {
        setPhase('scanning')
      }
    }).catch((error) => {
      if (disposed || runtimeFailed || generation !== sessionGenerationRef.current) {
        return
      }

      readerReadyRef.current = false
      setFailureReason(classifyShopCameraFailure(error, window.isSecureContext))
    })

    return () => {
      disposed = true
      abortController.abort()
      sessionGenerationRef.current += 1
      readerReadyRef.current = false
      processingRef.current = false
      waitingForPickerRef.current = false
      pickerPauseSeenRef.current = false
      queueRef.current.reset()
      readerControlsRef.current?.stop()
      readerControlsRef.current = null
    }
  }, [drainQueue, refreshQueueSnapshot, retryVersion, sessionKey])

  useEffect(() => {
    const handleVisibilityChange = () => {
      if (document.hidden) {
        onRequestClose()
      }
    }

    document.addEventListener('visibilitychange', handleVisibilityChange)
    return () => document.removeEventListener('visibilitychange', handleVisibilityChange)
  }, [onRequestClose])

  const statusLabel = failureReason
    ? t('shop.scan.cameraError', 'Camera unavailable')
    : phase === 'starting'
      ? t('shop.scan.cameraStarting', 'Starting camera')
      : phase === 'processing'
        ? t('shop.scan.cameraProcessing', 'Processing')
        : phase === 'waiting'
          ? t('shop.scan.cameraWaitingSelection', 'Waiting for selection')
          : t('shop.scan.cameraScanning', 'Scanning')

  const failureMessage = failureReason
    ? t(`shop.scan.cameraFailure.${failureReason}`)
    : ''

  return (
    <div className="shop-camera-scanner" role="region" aria-label={t('shop.scan.cameraPreview', 'Camera preview')}>
      <div className="shop-camera-viewport">
        <video
          ref={videoRef}
          className="shop-camera-video"
          aria-label={t('shop.scan.cameraPreview', 'Camera preview')}
          autoPlay
          muted
          playsInline
        />
        {!failureReason ? <div className="shop-camera-frame" aria-hidden="true" /> : null}
        {phase === 'starting' && !failureReason ? (
          <div className="shop-camera-overlay">
            <Spin />
            <Text>{t('shop.scan.cameraStarting', 'Starting camera')}</Text>
          </div>
        ) : null}
        {failureReason ? (
          <div className="shop-camera-overlay shop-camera-overlay-error">
            <Text strong>{failureMessage}</Text>
            <Button icon={<ReloadOutlined />} onClick={() => setRetryVersion((current) => current + 1)}>
              {t('shop.scan.cameraRetry', 'Try Again')}
            </Button>
          </div>
        ) : null}
      </div>

      <div className="shop-camera-status" aria-live="polite">
        <div className="shop-camera-status-main">
          <Tag color={failureReason ? 'error' : phase === 'waiting' ? 'gold' : 'processing'}>{statusLabel}</Tag>
          <Text type="secondary">
            {queueFull
              ? t('shop.scan.cameraQueueFull', 'The scan queue is full. Please wait.')
              : failureReason
                ? failureMessage
                : t('shop.scan.cameraHint', 'Hold an EAN-13 or Code 128 barcode inside the frame.')}
          </Text>
        </div>
        <Tag>{t('shop.scan.cameraQueue', { count: queueSnapshot.pendingCount })}</Tag>
      </div>
    </div>
  )
}
