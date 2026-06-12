import { useEffect, useRef } from 'react'

interface UseBarcodeScannerOptions {
  enabled: boolean
  minLength?: number
  idleMs?: number
  resetMs?: number
  onScan?: (value: string) => void
}

function isEditableElement(target: EventTarget | null) {
  if (!(target instanceof HTMLElement)) {
    return false
  }

  if (target.isContentEditable) {
    return true
  }

  const tagName = target.tagName.toLowerCase()
  if (tagName === 'input' || tagName === 'textarea' || tagName === 'select') {
    return true
  }

  return Boolean(target.closest('input, textarea, select, [contenteditable="true"], .ant-input, .ant-input-affix-wrapper'))
}

export function useBarcodeScanner({
  enabled,
  minLength = 3,
  idleMs = 300,
  resetMs = 2000,
  onScan,
}: UseBarcodeScannerOptions) {
  const onScanRef = useRef(onScan)
  onScanRef.current = onScan

  const bufferRef = useRef('')
  const timerRef = useRef<number | null>(null)
  const startedAtRef = useRef<number | null>(null)

  useEffect(() => {
    const clearTimer = () => {
      if (timerRef.current !== null) {
        window.clearTimeout(timerRef.current)
        timerRef.current = null
      }
    }

    const resetBuffer = () => {
      clearTimer()
      bufferRef.current = ''
      startedAtRef.current = null
    }

    const submit = () => {
      clearTimer()
      const value = bufferRef.current.trim()
      bufferRef.current = ''
      startedAtRef.current = null

      if (value.length >= minLength) {
        setTimeout(() => onScanRef.current?.(value), 0)
      }
    }

    const handleIdle = () => {
      clearTimer()

      const value = bufferRef.current.trim()
      if (!value) {
        resetBuffer()
        return
      }

      if (value.length >= minLength) {
        submit()
        return
      }

      const elapsed = startedAtRef.current ? Date.now() - startedAtRef.current : 0
      if (elapsed >= resetMs) {
        resetBuffer()
        return
      }

      timerRef.current = window.setTimeout(handleIdle, idleMs)
    }

    const scheduleFlush = () => {
      clearTimer()
      timerRef.current = window.setTimeout(handleIdle, idleMs)
    }

    if (!enabled) {
      resetBuffer()
      return resetBuffer
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) {
        return
      }

      if (isEditableElement(event.target)) {
        resetBuffer()
        return
      }

      if (event.key === 'Enter') {
        if (bufferRef.current) {
          submit()
        }
        return
      }

      if (event.key === 'Escape') {
        resetBuffer()
        return
      }

      if (event.key.length !== 1) {
        return
      }

      if (!bufferRef.current) {
        startedAtRef.current = Date.now()
      }

      bufferRef.current += event.key
      scheduleFlush()
    }

    window.addEventListener('keydown', handleKeyDown, true)

    return () => {
      window.removeEventListener('keydown', handleKeyDown, true)
      resetBuffer()
    }
  }, [enabled, idleMs, minLength, resetMs])
}
