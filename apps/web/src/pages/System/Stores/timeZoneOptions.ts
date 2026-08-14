export const storeTimeZoneOptions = [
  { value: 'Australia/Brisbane', label: 'Australia/Brisbane (Queensland)' },
  { value: 'Australia/Sydney', label: 'Australia/Sydney (New South Wales)' },
  { value: 'Australia/Melbourne', label: 'Australia/Melbourne (Victoria)' },
]

export const UNSET_STORE_TIME_ZONE_FILTER = '__unset__'

export function formatStoreTimeZoneId(timeZoneId?: string) {
  if (!timeZoneId) {
    return '--'
  }

  return storeTimeZoneOptions.find((option) => option.value === timeZoneId)?.label ?? timeZoneId
}
