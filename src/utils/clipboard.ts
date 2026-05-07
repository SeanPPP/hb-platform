import { message } from 'antd'

export async function copyTextToClipboard(text?: string) {
  if (!text) {
    return false
  }

  try {
    await navigator.clipboard.writeText(text)
    message.success('复制成功')
    return true
  } catch (error) {
    console.error(error)
    message.error('复制失败，请手动复制')
    return false
  }
}
