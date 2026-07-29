// apps/web/src/pages/PosAdmin/DeviceRegistration/deviceSystemOptions.ts
var REGISTERED_DEVICE_SYSTEM_OPTIONS = ["Windows", "iPadOS", "Other"];
var APP_DEVICE_SYSTEM_OPTIONS = ["Android", "iOS", "iPadOS", "Windows", "Mac"];
var EDITABLE_DEVICE_SYSTEM_OPTIONS = ["Android", "iOS", "iPadOS", "Windows", "Mac"];

// apps/web/src/pages/PosAdmin/DeviceRegistration/deviceSystemOptions.test.ts
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
assertDeepEqual(
  REGISTERED_DEVICE_SYSTEM_OPTIONS,
  ["Windows", "iPadOS", "Other"],
  "Registered device filter should expose the platform categories"
);
assertDeepEqual(
  APP_DEVICE_SYSTEM_OPTIONS,
  ["Android", "iOS", "iPadOS", "Windows", "Mac"],
  "App usage filter should retain mobile platforms and include iPadOS"
);
assertDeepEqual(
  EDITABLE_DEVICE_SYSTEM_OPTIONS,
  ["Android", "iOS", "iPadOS", "Windows", "Mac"],
  "Existing device edit workflow should allow iPadOS"
);
console.log("deviceSystemOptions.test: ok");
