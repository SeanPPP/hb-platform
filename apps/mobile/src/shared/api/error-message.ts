type ApiRecord = Record<string, unknown>;

function isRecord(value: unknown): value is ApiRecord {
  return typeof value === "object" && value !== null;
}

function asTrimmedString(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function collectDetailMessages(value: unknown, messages: string[] = []) {
  if (!value) {
    return messages;
  }

  if (typeof value === "string") {
    const trimmed = value.trim();
    if (trimmed) {
      messages.push(trimmed);
    }
    return messages;
  }

  if (Array.isArray(value)) {
    value.forEach((item) => collectDetailMessages(item, messages));
    return messages;
  }

  if (isRecord(value)) {
    Object.values(value).forEach((item) => collectDetailMessages(item, messages));
  }

  return messages;
}

function uniqueMessages(messages: string[]) {
  return messages.filter((message, index) => messages.indexOf(message) === index);
}

export function extractApiErrorMessage(error: unknown, fallback = "Request failed") {
  const responseData = isRecord(error) && isRecord(error.response)
    ? error.response.data
    : null;
  const payload = responseData ?? error;

  if (isRecord(payload)) {
    const detailMessages = uniqueMessages(collectDetailMessages(payload.details ?? payload.errors));
    if (detailMessages.length) {
      return detailMessages.join("\n");
    }

    const envelopeMessage = asTrimmedString(payload.message ?? payload.Message);
    if (envelopeMessage) {
      return envelopeMessage;
    }
  }

  if (error instanceof Error && error.message.trim()) {
    return error.message;
  }

  return fallback;
}
