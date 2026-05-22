import { EmptyState } from "./EmptyState";

function noop() {
  return undefined;
}

const legacyAction = (
  <EmptyState title="No data" description="Try again later." actionLabel="Retry" onAction={noop} />
);

const dualActions = (
  <EmptyState
    title="Load failed"
    description="The page can be retried or left."
    primaryAction={{ label: "Retry", onPress: noop, icon: "refresh" }}
    secondaryAction={{ label: "Back", onPress: noop, icon: "arrow-left", mode: "outlined" }}
  />
);

void legacyAction;
void dualActions;
