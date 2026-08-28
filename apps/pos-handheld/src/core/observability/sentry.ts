import * as Sentry from "@sentry/react-native";
import * as Application from "expo-application";
import Constants from "expo-constants";

import {
  resolveSentryConfiguration,
  sanitizeSentryEvent,
} from "./sentry-config";

const configuration = resolveSentryConfiguration({
  dsn: process.env.EXPO_PUBLIC_HBPOS_SENTRY_DSN,
  appIdentifier: "com.hbweb.poshandheld",
  appVersion:
    Application.nativeApplicationVersion ??
    Constants.nativeAppVersion ??
    Constants.expoConfig?.version ??
    "0.0.0",
  buildNumber:
    Application.nativeBuildVersion ?? Constants.nativeBuildVersion,
  environment:
    process.env.EXPO_PUBLIC_HBPOS_SENTRY_ENVIRONMENT ??
    process.env.EXPO_PUBLIC_HBPOS_LOG_CENTER_ENVIRONMENT ??
    process.env.EXPO_PUBLIC_HBPOS_BUILD_PROFILE,
});

const dsn = configuration.options.dsn;
if (configuration.enabled && dsn) {
  Sentry.init({
    ...configuration.options,
    dsn,
    enableAutoSessionTracking: true,
    enableAutoPerformanceTracing: false,
    enableAppStartTracking: false,
    enableNativeFramesTracking: false,
    enableStallTracking: false,
    enableUserInteractionTracing: false,
    enableCaptureFailedRequests: false,
    attachScreenshot: false,
    attachViewHierarchy: false,
    tracesSampleRate: 0,
    profilesSampleRate: 0,
    beforeSend: (event) => sanitizeSentryEvent(event),
    beforeBreadcrumb: (breadcrumb) => sanitizeSentryEvent(breadcrumb),
  });
}
