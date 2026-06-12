import { message } from 'antd'

interface CopyTextToClipboardOptions {
  successMessage?: string
  failureMessage?: string
}

function copyTextWithTextArea(text: string) {
  if (typeof document === 'undefined') {
    return false
  }

  const textarea = document.createElement('textarea')
  textarea.value = text
  textarea.setAttribute('readonly', '')
  textarea.style.position = 'fixed'
  textarea.style.top = '-1000px'
  textarea.style.left = '-1000px'
  textarea.style.opacity = '0'

  document.body.appendChild(textarea)
  textarea.focus()
  textarea.select()
  textarea.setSelectionRange(0, text.length)

  try {
    return document.execCommand('copy')
  } finally {
    document.body.removeChild(textarea)
  }
}

export async function copyTextToClipboard(text?: string, options: CopyTextToClipboardOptions = {}) {
  if (!text) {
    return false
  }

  const successMessage = options.successMessage ?? '复制成功'
  const failureMessage = options.failureMessage ?? '复制失败，请手动复制'

  try {
    if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      message.success(successMessage)
      return true
    }

    if (copyTextWithTextArea(text)) {
      message.success(successMessage)
      return true
    }

    message.error(failureMessage)
    return false
  } catch (error) {
    if (copyTextWithTextArea(text)) {
      message.success(successMessage)
      return true
    }

    console.error(error)
    message.error(failureMessage)
    return false
  }
}
