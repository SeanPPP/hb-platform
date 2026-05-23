export type UnauthenticatedSessionEvent = {
  message?: string;
};

type Listener = (event: UnauthenticatedSessionEvent) => void;

const listeners = new Set<Listener>();

export function subscribeUnauthenticatedSession(listener: Listener) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function emitUnauthenticatedSession(event: UnauthenticatedSessionEvent = {}) {
  listeners.forEach((listener) => listener(event));
}
