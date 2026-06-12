const DEFAULT_TAG_COLORS = [
  'red',
  'orange',
  'gold',
  'lime',
  'green',
  'cyan',
  'blue',
  'purple',
  'magenta',
  'volcano',
]

function hashText(value: string) {
  let hash = 0
  for (let index = 0; index < value.length; index += 1) {
    hash = (hash << 5) - hash + value.charCodeAt(index)
    hash |= 0
  }
  return hash
}

export function getStableTagColor(value: string, colors: string[] = DEFAULT_TAG_COLORS): string {
  if (!value) {
    return 'default'
  }

  return colors[Math.abs(hashText(value)) % colors.length] || 'default'
}

export function getDateTagColor(value: string): string {
  return getStableTagColor(value)
}
