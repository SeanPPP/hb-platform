export type ScanFeedbackTone = 'success' | 'multiple' | 'not-found' | 'blocked' | 'error'

let audioContext: AudioContext | null = null
let unlocked = false

function getAudioContext() {
  if (typeof window === 'undefined') {
    return null
  }

  const Context = window.AudioContext || (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
  if (!Context) {
    return null
  }

  if (!audioContext) {
    audioContext = new Context()
  }

  return audioContext
}

function playTone(frequency: number, duration: number, delay = 0) {
  const context = getAudioContext()
  if (!context || context.state !== 'running') {
    return
  }

  const oscillator = context.createOscillator()
  const gainNode = context.createGain()
  const startAt = context.currentTime + delay
  const endAt = startAt + duration

  oscillator.type = 'sine'
  oscillator.frequency.setValueAtTime(frequency, startAt)
  gainNode.gain.setValueAtTime(0.0001, startAt)
  gainNode.gain.exponentialRampToValueAtTime(0.14, startAt + 0.01)
  gainNode.gain.exponentialRampToValueAtTime(0.0001, endAt)

  oscillator.connect(gainNode)
  gainNode.connect(context.destination)
  oscillator.start(startAt)
  oscillator.stop(endAt)
}

export function isScanFeedbackUnlocked() {
  return unlocked
}

export async function unlockScanFeedback() {
  const context = getAudioContext()
  if (!context) {
    return false
  }

  await context.resume()
  unlocked = context.state === 'running'

  if (unlocked) {
    playTone(880, 0.04)
  }

  return unlocked
}

export function playScanFeedback(type: ScanFeedbackTone) {
  const context = getAudioContext()
  if (!context || !unlocked || context.state !== 'running') {
    return false
  }

  switch (type) {
    case 'success':
      playTone(900, 0.05)
      playTone(1240, 0.06, 0.05)
      break
    case 'multiple':
      playTone(780, 0.05)
      playTone(780, 0.05, 0.08)
      break
    case 'not-found':
      playTone(420, 0.12)
      break
    case 'blocked':
      playTone(320, 0.08)
      playTone(250, 0.08, 0.09)
      break
    case 'error':
      playTone(260, 0.1)
      playTone(210, 0.12, 0.08)
      break
  }

  return true
}
