import type { WarehouseLocation } from "./types";

export type WarehouseLocationCodePart = "letter" | "section" | "shelf" | "slot";
export type WarehouseLocationCodeParts = Record<WarehouseLocationCodePart, string>;

export function buildWarehouseLocationCode(parts: WarehouseLocationCodeParts) {
  return `${parts.letter}-${parts.section}-${parts.shelf}-${parts.slot}`;
}

function parseWarehouseLocationCode(code?: string | null): WarehouseLocationCodeParts | null {
  const normalized = (code ?? "").trim().toUpperCase();
  const match = /^([A-Z])-(\d{2})-(\d{2})-(\d{2})$/.exec(normalized);
  if (!match) {
    return null;
  }

  return {
    letter: match[1],
    section: match[2],
    shelf: match[3],
    slot: match[4],
  };
}

export function splitWarehouseLocationCode(code?: string | null): WarehouseLocationCodeParts {
  return parseWarehouseLocationCode(code) ?? { letter: "A", section: "00", shelf: "00", slot: "01" };
}

export function getAvailableWarehouseLocationSlots(
  locations: WarehouseLocation[],
  parts: WarehouseLocationCodeParts,
  slotOptions: string[],
  excludeLocationGuid?: string | null
) {
  const occupiedSlots = new Set<string>();

  for (const location of locations) {
    if (excludeLocationGuid && location.locationGuid === excludeLocationGuid) {
      continue;
    }

    const candidate = parseWarehouseLocationCode(location.locationCode);
    if (!candidate) {
      continue;
    }

    if (
      candidate.letter === parts.letter
      && candidate.section === parts.section
      && candidate.shelf === parts.shelf
    ) {
      occupiedSlots.add(candidate.slot);
    }
  }

  return slotOptions.filter((slot) => !occupiedSlots.has(slot));
}

export function resolveAvailableWarehouseLocationParts(
  locations: WarehouseLocation[],
  parts: WarehouseLocationCodeParts,
  slotOptions: string[],
  excludeLocationGuid?: string | null
) {
  const availableSlots = getAvailableWarehouseLocationSlots(locations, parts, slotOptions, excludeLocationGuid);
  if (!availableSlots.length || availableSlots.includes(parts.slot)) {
    return parts;
  }

  return { ...parts, slot: availableSlots[0] };
}
