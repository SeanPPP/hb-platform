export type IsoClock = () => string;
export type IdFactory = () => string;

export function createFixedIsoClock(input: string): IsoClock {
  const milliseconds = Date.parse(input);
  if (!Number.isFinite(milliseconds)) {
    throw new TypeError("Fixed clock requires a valid ISO date-time.");
  }
  const normalized = new Date(milliseconds).toISOString();
  return () => normalized;
}

export function createSequenceIdFactory(input: readonly string[]): IdFactory {
  const ids = Object.freeze([...input]);
  if (
    ids.length === 0 ||
    ids.some((id) => typeof id !== "string" || id.trim().length === 0)
  ) {
    throw new TypeError("Sequence IDs must be a non-empty list of non-empty strings.");
  }

  let index = 0;
  return () => {
    const id = ids[index];
    if (id === undefined) {
      throw new Error("Sequence ID fixture exhausted.");
    }
    index += 1;
    return id;
  };
}
