import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  AccessibilityInfo,
  Animated,
  ScrollView,
  StyleSheet,
  Text,
  View,
  type GestureResponderEvent,
  type LayoutChangeEvent,
  type StyleProp,
  type ViewStyle,
} from "react-native";

import { MIN_TOUCH_TARGET } from "./sales-presenter";
import {
  mergeVisibleSalesToolbarOrder,
  reconcileSalesToolbarOrder,
  type SalesToolbarActionId,
} from "./sales-toolbar-order";

import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

export type SalesToolbarActionTone =
  | "primary"
  | "secondary"
  | "danger"
  | "quiet";

export type SalesToolbarAction = Readonly<{
  id: SalesToolbarActionId;
  label: string;
  onPress(): void;
  disabled?: boolean;
  tone?: SalesToolbarActionTone;
  testID?: string;
  accessibilityLabel?: string;
}>;

export type SalesToolbarAccessibilityCopy = Readonly<{
  moveEarlier: string;
  moveLater: string;
  reorderHint: string;
  positionChanged(label: string, position: number, total: number): string;
}>;

export type SalesToolbarProps = Readonly<{
  actions: readonly SalesToolbarAction[];
  canonicalOrder: readonly SalesToolbarActionId[];
  onOrderChange(order: readonly SalesToolbarActionId[]): void;
  accessibilityCopy?: SalesToolbarAccessibilityCopy;
  style?: StyleProp<ViewStyle>;
  testID?: string;
}>;

type ToolbarLayout = Readonly<{
  height: number;
  width: number;
  x: number;
  y: number;
}>;

type DragOrigin = Readonly<{
  centerX: number;
  centerY: number;
  pageX: number;
  pageY: number;
}>;

type ToolbarSlot = Readonly<{
  centerX: number;
  centerY: number;
  index: number;
}>;

type ToolbarScrollMetrics = Readonly<{
  contentWidth: number;
  offsetX: number;
  viewportWidth: number;
}>;

const LONG_PRESS_DELAY_MS = 400;
const MOVEMENT_TOLERANCE = 8;
const OVERFLOW_EPSILON = 2;
const defaultAccessibilityCopy: SalesToolbarAccessibilityCopy = {
  moveEarlier: "Move earlier",
  moveLater: "Move later",
  reorderHint:
    "Long press and drag to reorder. You can also use Move earlier or Move later.",
  positionChanged: (label, position, total) =>
    `${label} moved to position ${position} of ${total}.`,
};

export function SalesToolbar({
  actions,
  canonicalOrder,
  onOrderChange,
  accessibilityCopy = defaultAccessibilityCopy,
  style,
  testID,
}: SalesToolbarProps) {
  const [reduceMotion, setReduceMotion] = useState(false);
  const [draggingId, setDraggingId] = useState<SalesToolbarActionId | null>(
    null,
  );
  const dragFeedback = useRef(new Animated.Value(0)).current;
  const layoutsRef = useRef(new Map<SalesToolbarActionId, ToolbarLayout>());
  const dragOriginRef = useRef<DragOrigin | null>(null);
  const dragSlotsRef = useRef<readonly ToolbarSlot[]>([]);
  const dragStartOrderRef = useRef<readonly SalesToolbarActionId[]>([]);
  const draggingIdRef = useRef<SalesToolbarActionId | null>(null);
  const movedBeforeLongPressRef = useRef(false);
  const suppressBusinessPressRef = useRef(false);
  const scrollMetricsRef = useRef<ToolbarScrollMetrics>({
    contentWidth: 0,
    offsetX: 0,
    viewportWidth: 0,
  });
  const [hiddenSides, setHiddenSides] = useState({
    left: false,
    right: false,
  });

  const canonical = useMemo(
    () => reconcileSalesToolbarOrder(canonicalOrder),
    [canonicalOrder],
  );
  const canonicalKey = canonical.join("|");
  const actionMap = useMemo(() => {
    const next = new Map<SalesToolbarActionId, SalesToolbarAction>();
    for (const action of actions) {
      if (!next.has(action.id)) next.set(action.id, action);
    }
    return next;
  }, [actions]);
  const visibleIds = useMemo(
    () => canonical.filter((actionId) => actionMap.has(actionId)),
    [actionMap, canonical],
  );
  const visibleIdsKey = visibleIds.join("|");
  const initialVisibleOrder = useMemo(
    () => [...visibleIds],
    [visibleIds],
  );
  const [visibleOrder, setVisibleOrder] = useState(initialVisibleOrder);
  const visibleOrderRef = useRef<readonly SalesToolbarActionId[]>(
    initialVisibleOrder,
  );
  const propsKeyRef = useRef(`${canonicalKey}:${visibleIdsKey}`);

  useEffect(() => {
    const nextPropsKey = `${canonicalKey}:${visibleIdsKey}`;
    if (propsKeyRef.current === nextPropsKey) return;
    propsKeyRef.current = nextPropsKey;
    const nextVisibleOrder = [...visibleIds];
    visibleOrderRef.current = nextVisibleOrder;
    setVisibleOrder(nextVisibleOrder);
  }, [canonicalKey, visibleIds, visibleIdsKey]);

  useEffect(() => {
    let mounted = true;
    void AccessibilityInfo.isReduceMotionEnabled().then((enabled) => {
      // 默认值已是 false，避免每次挂载都进行一次无变化的状态更新。
      if (mounted && enabled) setReduceMotion(true);
    });
    const subscription = AccessibilityInfo.addEventListener(
      "reduceMotionChanged",
      setReduceMotion,
    );
    return () => {
      mounted = false;
      subscription.remove();
    };
  }, []);

  const orderedVisibleIds = orderVisibleIds(visibleOrder, visibleIds);

  const setFeedback = useCallback(
    (active: boolean) => {
      if (reduceMotion) {
        dragFeedback.setValue(active ? 1 : 0);
        return;
      }
      Animated.timing(dragFeedback, {
        duration: active ? 120 : 90,
        toValue: active ? 1 : 0,
        useNativeDriver: true,
      }).start();
    },
    [dragFeedback, reduceMotion],
  );

  const setLocalVisibleOrder = useCallback(
    (next: readonly SalesToolbarActionId[]) => {
      if (sameOrder(next, visibleOrderRef.current)) return;
      const stableNext = [...next];
      visibleOrderRef.current = stableNext;
      setVisibleOrder(stableNext);
    },
    [],
  );

  const moveVisibleActionToIndex = useCallback(
    (actionId: SalesToolbarActionId, targetIndex: number) => {
      const current = visibleOrderRef.current;
      const sourceIndex = current.indexOf(actionId);
      if (
        sourceIndex < 0 ||
        targetIndex < 0 ||
        targetIndex >= current.length ||
        sourceIndex === targetIndex
      ) {
        return false;
      }
      const next = [...current];
      const [moved] = next.splice(sourceIndex, 1);
      if (!moved) return false;
      next.splice(targetIndex, 0, moved);
      setLocalVisibleOrder(next);
      return true;
    },
    [setLocalVisibleOrder],
  );

  const moveVisibleAction = useCallback(
    (actionId: SalesToolbarActionId, targetId: SalesToolbarActionId) =>
      moveVisibleActionToIndex(
        actionId,
        visibleOrderRef.current.indexOf(targetId),
      ),
    [moveVisibleActionToIndex],
  );

  const commitAccessibleMove = useCallback(
    (actionId: SalesToolbarActionId, direction: -1 | 1) => {
      const current = visibleOrderRef.current;
      const sourceIndex = current.indexOf(actionId);
      const targetId = current[sourceIndex + direction];
      if (sourceIndex < 0 || !targetId) return;
      if (!moveVisibleAction(actionId, targetId)) return;
      const nextPosition = visibleOrderRef.current.indexOf(actionId) + 1;
      const actionLabel = actionMap.get(actionId)?.label ?? actionId;
      AccessibilityInfo.announceForAccessibility(
        accessibilityCopy.positionChanged(
          actionLabel,
          nextPosition,
          visibleOrderRef.current.length,
        ),
      );

      const nextCanonical = mergeVisibleSalesToolbarOrder(
        canonical,
        visibleOrderRef.current,
      );
      if (!sameOrder(nextCanonical, canonical)) onOrderChange(nextCanonical);
    },
    [
      accessibilityCopy,
      actionMap,
      canonical,
      moveVisibleAction,
      onOrderChange,
    ],
  );

  const finishDrag = useCallback(() => {
    const draggedId = draggingIdRef.current;
    if (!draggedId) return;

    draggingIdRef.current = null;
    setDraggingId(null);
    setFeedback(false);
    if (!sameOrder(dragStartOrderRef.current, visibleOrderRef.current)) {
      const nextCanonical = mergeVisibleSalesToolbarOrder(
        canonical,
        visibleOrderRef.current,
      );
      if (!sameOrder(nextCanonical, canonical)) onOrderChange(nextCanonical);
    }
    dragOriginRef.current = null;
    dragSlotsRef.current = [];
  }, [canonical, onOrderChange, setFeedback]);

  const cancelDrag = useCallback(() => {
    if (!draggingIdRef.current) return;

    draggingIdRef.current = null;
    setDraggingId(null);
    setLocalVisibleOrder(dragStartOrderRef.current);
    setFeedback(false);
    dragOriginRef.current = null;
    dragSlotsRef.current = [];
  }, [setFeedback, setLocalVisibleOrder]);

  const beginPointer = useCallback(
    (actionId: SalesToolbarActionId, event: GestureResponderEvent) => {
      const layout = layoutsRef.current.get(actionId);
      const { pageX, pageY } = event.nativeEvent;
      dragOriginRef.current = layout
        ? {
            centerX: layout.x + layout.width / 2,
            centerY: layout.y + layout.height / 2,
            pageX,
            pageY,
          }
        : null;
      movedBeforeLongPressRef.current = false;
      suppressBusinessPressRef.current = false;
    },
    [],
  );

  const beginDrag = useCallback(
    (actionId: SalesToolbarActionId) => {
      if (movedBeforeLongPressRef.current || !dragOriginRef.current) return;
      suppressBusinessPressRef.current = true;
      draggingIdRef.current = actionId;
      dragStartOrderRef.current = [...visibleOrderRef.current];
      // 拖动期间按开始时的物理槽位命中，避免 React 布局回调到达前重复 move 造成来回抖动。
      dragSlotsRef.current = visibleOrderRef.current.flatMap(
        (visibleId, index) => {
          const layout = layoutsRef.current.get(visibleId);
          return layout
            ? [
                {
                  centerX: layout.x + layout.width / 2,
                  centerY: layout.y + layout.height / 2,
                  index,
                },
              ]
            : [];
        },
      );
      setDraggingId(actionId);
      setFeedback(true);
    },
    [setFeedback],
  );

  const moveDrag = useCallback(
    (event: GestureResponderEvent) => {
      const origin = dragOriginRef.current;
      const { pageX, pageY } = event.nativeEvent;
      if (!origin) return;

      const distance = Math.hypot(pageX - origin.pageX, pageY - origin.pageY);
      const activeId = draggingIdRef.current;
      if (!activeId) {
        if (distance > MOVEMENT_TOLERANCE) {
          movedBeforeLongPressRef.current = true;
        }
        return;
      }

      const pointerX = origin.centerX + pageX - origin.pageX;
      const pointerY = origin.centerY + pageY - origin.pageY;
      const targetIndex = findNearestSlotIndex(
        pointerX,
        pointerY,
        dragSlotsRef.current,
      );
      if (targetIndex >= 0) {
        moveVisibleActionToIndex(activeId, targetIndex);
      }
    },
    [moveVisibleActionToIndex],
  );

  const onLayout = useCallback(
    (actionId: SalesToolbarActionId, event: LayoutChangeEvent) => {
      const { height, width, x, y } = event.nativeEvent.layout;
      layoutsRef.current.set(actionId, { height, width, x, y });
    },
    [],
  );

  const updateScrollMetrics = useCallback(
    (partial: Partial<ToolbarScrollMetrics>) => {
      const next = { ...scrollMetricsRef.current, ...partial };
      scrollMetricsRef.current = next;
      const left = next.offsetX > OVERFLOW_EPSILON;
      const right =
        next.contentWidth - next.viewportWidth - next.offsetX >
        OVERFLOW_EPSILON;
      setHiddenSides((current) =>
        current.left === left && current.right === right
          ? current
          : { left, right },
      );
    },
    [],
  );

  return (
    <View style={[styles.toolbarFrame, style]}>
      <ScrollView
        contentContainerStyle={styles.toolbarContent}
        directionalLockEnabled
        horizontal
        onContentSizeChange={(contentWidth) =>
          updateScrollMetrics({ contentWidth })
        }
        onLayout={(event) =>
          updateScrollMetrics({
            viewportWidth: event.nativeEvent.layout.width,
          })
        }
        onScroll={(event) =>
          updateScrollMetrics({
            contentWidth: event.nativeEvent.contentSize.width,
            offsetX: Math.max(0, event.nativeEvent.contentOffset.x),
            viewportWidth: event.nativeEvent.layoutMeasurement.width,
          })
        }
        scrollEnabled={draggingId === null}
        scrollEventThrottle={16}
        showsHorizontalScrollIndicator={false}
        style={styles.toolbarViewport}
        testID={testID ?? "sales-toolbar"}
      >
        {orderedVisibleIds.map((actionId) => {
          const action = actionMap.get(actionId);
          if (!action) return null;
          const isDragging = draggingId === action.id;
          const actionIndex = orderedVisibleIds.indexOf(action.id);
          const accessibilityActions = [
            ...(actionIndex > 0
              ? [
                  {
                    label: accessibilityCopy.moveEarlier,
                    name: "move-earlier",
                  },
                ]
              : []),
            ...(actionIndex < orderedVisibleIds.length - 1
              ? [
                  {
                    label: accessibilityCopy.moveLater,
                    name: "move-later",
                  },
                ]
              : []),
          ];
          // RN 0.81 的 Pressable 运行时支持 onPressMove，但当前 TypeScript 声明遗漏该属性。
          const pressMoveProps = { onPressMove: moveDrag };
          return (
            <Animated.View
              key={action.id}
              onLayout={(event) => onLayout(action.id, event)}
              style={[
                styles.actionShell,
                isDragging && styles.actionShellDragging,
                isDragging && {
                  opacity: dragFeedback.interpolate({
                    inputRange: [0, 1],
                    outputRange: [1, 0.82],
                  }),
                  transform: [
                    {
                      translateY: dragFeedback.interpolate({
                        inputRange: [0, 1],
                        outputRange: [0, -2],
                      }),
                    },
                  ],
                },
              ]}
              testID={`${action.testID ?? `sales-toolbar-${action.id}`}-layout`}
            >
              <PosPressable
                {...pressMoveProps}
                accessibilityActions={accessibilityActions}
                accessibilityHint={accessibilityCopy.reorderHint}
                accessibilityLabel={action.accessibilityLabel ?? action.label}
                accessibilityRole="button"
                accessibilityState={{ disabled: action.disabled === true }}
                cancelable={!isDragging}
                delayLongPress={LONG_PRESS_DELAY_MS}
                onAccessibilityAction={(event) => {
                  const direction =
                    event.nativeEvent.actionName === "move-earlier"
                      ? -1
                      : event.nativeEvent.actionName === "move-later"
                        ? 1
                        : null;
                  if (direction) commitAccessibleMove(action.id, direction);
                }}
                onLongPress={() => beginDrag(action.id)}
                onPress={() => {
                  const suppress = suppressBusinessPressRef.current;
                  suppressBusinessPressRef.current = false;
                  if (!suppress && !action.disabled) action.onPress();
                }}
                onPressIn={(event) => beginPointer(action.id, event)}
                onTouchCancel={cancelDrag}
                onTouchEnd={finishDrag}
                longPressSound="navigate"
                sound="navigate"
                style={({ pressed }) => [
                  styles.actionButton,
                  actionToneStyles[action.tone ?? "primary"],
                  action.disabled && styles.actionButtonDisabled,
                  pressed && !isDragging && styles.actionButtonPressed,
                ]}
                testID={action.testID ?? `sales-toolbar-${action.id}`}
              >
                <Text
                  style={[
                    styles.actionText,
                    (action.tone === "quiet" ||
                      action.tone === "secondary") &&
                      styles.quietActionText,
                    action.tone === "danger" && styles.dangerActionText,
                    action.disabled && styles.actionTextDisabled,
                  ]}
                >
                  {action.label}
                </Text>
              </PosPressable>
            </Animated.View>
          );
        })}
      </ScrollView>
      {hiddenSides.left ? (
        <View
          accessible={false}
          pointerEvents="none"
          style={[styles.overflowHint, styles.overflowHintLeft]}
          testID="sales-toolbar-hidden-left"
        >
          <Text style={styles.overflowHintIcon}>‹</Text>
        </View>
      ) : null}
      {hiddenSides.right ? (
        <View
          accessible={false}
          pointerEvents="none"
          style={[styles.overflowHint, styles.overflowHintRight]}
          testID="sales-toolbar-hidden-right"
        >
          <Text style={styles.overflowHintIcon}>›</Text>
        </View>
      ) : null}
    </View>
  );
}

function orderVisibleIds(
  current: readonly SalesToolbarActionId[],
  visibleIds: readonly SalesToolbarActionId[],
): SalesToolbarActionId[] {
  const visibleSet = new Set(visibleIds);
  const ordered = current.filter((actionId) => visibleSet.has(actionId));
  const seen = new Set(ordered);
  for (const actionId of visibleIds) {
    if (seen.has(actionId)) continue;
    seen.add(actionId);
    ordered.push(actionId);
  }
  return ordered;
}

function findNearestSlotIndex(
  pointerX: number,
  pointerY: number,
  slots: readonly ToolbarSlot[],
): number {
  let nearestIndex = -1;
  let nearestDistance = Number.POSITIVE_INFINITY;
  for (const slot of slots) {
    const distance = Math.hypot(
      pointerX - slot.centerX,
      pointerY - slot.centerY,
    );
    if (distance >= nearestDistance) continue;
    nearestIndex = slot.index;
    nearestDistance = distance;
  }
  return nearestIndex;
}

function sameOrder(
  first: readonly SalesToolbarActionId[],
  second: readonly SalesToolbarActionId[],
): boolean {
  return first.length === second.length && first.every((id, index) => id === second[index]);
}

const actionToneStyles = StyleSheet.create({
  danger: {
    backgroundColor: posColors.redSoft,
    borderColor: posColors.red,
  },
  primary: {
    backgroundColor: posColors.orange,
    borderColor: posColors.orange,
  },
  quiet: {
    backgroundColor: "transparent",
    borderColor: posColors.border,
  },
  secondary: {
    backgroundColor: posColors.surface,
    borderColor: posColors.ink,
  },
});

const styles = StyleSheet.create({
  actionButton: {
    alignItems: "center",
    borderColor: posColors.orange,
    borderRadius: 4,
    borderWidth: 1,
    justifyContent: "center",
    minHeight: MIN_TOUCH_TARGET,
    minWidth: MIN_TOUCH_TARGET,
    paddingHorizontal: 14,
  },
  actionButtonDisabled: {
    backgroundColor: "#E4E1DA",
    borderColor: "#D0CCC2",
    opacity: 0.72,
  },
  actionButtonPressed: {
    opacity: 0.72,
  },
  actionShell: {
    minHeight: MIN_TOUCH_TARGET,
  },
  actionShellDragging: {
    zIndex: 1,
  },
  actionText: {
    color: "#FFFFFF",
    fontSize: 14,
    fontWeight: "800",
  },
  actionTextDisabled: {
    color: "#7C8287",
  },
  dangerActionText: {
    color: posColors.red,
  },
  quietActionText: {
    color: posColors.ink,
  },
  overflowHint: {
    alignItems: "center",
    backgroundColor: posColors.surface,
    borderColor: posColors.border,
    borderRadius: 14,
    borderWidth: 1,
    height: 28,
    justifyContent: "center",
    marginTop: -14,
    position: "absolute",
    shadowColor: "#000000",
    shadowOffset: { height: 1, width: 0 },
    shadowOpacity: 0.14,
    shadowRadius: 2,
    top: "50%",
    width: 28,
    zIndex: 2,
  },
  overflowHintLeft: {
    left: 2,
  },
  overflowHintIcon: {
    color: posColors.ink,
    fontSize: 22,
    fontWeight: "900",
    lineHeight: 24,
  },
  overflowHintRight: {
    right: 2,
  },
  toolbarContent: {
    alignItems: "center",
    flexGrow: 1,
    flexDirection: "row",
    flexWrap: "nowrap",
    gap: 10,
    justifyContent: "flex-end",
  },
  toolbarFrame: {
    flexShrink: 1,
    minHeight: MIN_TOUCH_TARGET,
    minWidth: 0,
    position: "relative",
  },
  toolbarViewport: {
    minHeight: MIN_TOUCH_TARGET,
    minWidth: 0,
    width: "100%",
  },
});
