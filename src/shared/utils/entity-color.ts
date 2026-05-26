export type EntityKind = "store" | "supplier";

export interface EntityTone {
  backgroundColor: string;
  borderColor: string;
  textColor: string;
}

const PALETTE: EntityTone[] = [
  { backgroundColor: "#E8F3FF", borderColor: "#91C3FF", textColor: "#0F5CB8" },
  { backgroundColor: "#EAFBF2", borderColor: "#92D8AE", textColor: "#18794E" },
  { backgroundColor: "#FFF4E6", borderColor: "#F7C58B", textColor: "#B25A00" },
  { backgroundColor: "#F4EEFF", borderColor: "#C7AEFF", textColor: "#6B3FD6" },
  { backgroundColor: "#FFF1F0", borderColor: "#FFB3AD", textColor: "#C5342D" },
  { backgroundColor: "#EAF7F8", borderColor: "#97D5DA", textColor: "#0F6E78" },
];

const NEUTRAL_TONE: EntityTone = {
  backgroundColor: "#F5F5F5",
  borderColor: "#D9D9D9",
  textColor: "#595959",
};

const KIND_OFFSET: Record<EntityKind, number> = {
  store: 0,
  supplier: 3,
};

function normalizeKey(key?: string | null) {
  return key?.trim().toLowerCase() ?? "";
}

function hashString(value: string) {
  let hash = 0;

  for (let index = 0; index < value.length; index += 1) {
    hash = (hash * 31 + value.charCodeAt(index)) >>> 0;
  }

  return hash;
}

export function getEntityTone(key: string | null | undefined, kind: EntityKind): EntityTone {
  const normalizedKey = normalizeKey(key);

  if (!normalizedKey) {
    return NEUTRAL_TONE;
  }

  const paletteIndex = (hashString(normalizedKey) + KIND_OFFSET[kind]) % PALETTE.length;
  return PALETTE[paletteIndex];
}
