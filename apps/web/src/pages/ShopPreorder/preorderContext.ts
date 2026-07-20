export interface PreorderRequestContext {
  generation: number
  activationGuid: string
  storeCode: string
  key: string
}

export function createPreorderRequestContext(
  generation: number,
  activationGuid: string,
  storeCode: string,
): PreorderRequestContext {
  return {
    generation,
    activationGuid,
    storeCode,
    key: `${activationGuid}:${storeCode}`,
  }
}

export function isSamePreorderRequestContext(
  active: PreorderRequestContext | null,
  candidate: PreorderRequestContext | null,
) {
  return Boolean(
    active &&
    candidate &&
    active.generation === candidate.generation &&
    active.key === candidate.key,
  )
}
