import assert from "node:assert/strict";
import { createEmployeeProfileReviewAppStateHandler } from "./privacy-state";

const events: string[] = [];
const handleAppState = createEmployeeProfileReviewAppStateHandler({
  onInactive: () => events.push("inactive"),
  onActive: () => events.push("active"),
});

handleAppState("inactive");
handleAppState("background");
handleAppState("active");
assert.deepEqual(events, ["inactive", "inactive", "active"]);
