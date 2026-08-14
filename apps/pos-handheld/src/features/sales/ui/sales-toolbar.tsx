import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  AccessibilityInfo,
  Animated,
  Modal,
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
import { HandheldActionButton } from "@/ui/handheld/handheld-actions";
import { HandheldStateSurface } from "@/ui/handheld/handheld-design-states";
import { posColors } from "@/ui/theme";

export type SalesToolbarActionTone =
  "primary" | "secondary" | "danger" | "quiet";

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
  closeLabel?: string;
  style?: StyleProp<ViewStyle>;
  testID?: string;
  triggerLabel?: string;
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

const LONG_PRESS_DELAY_MS = 400;
const MOVEMENT_TOLERANCE = 8;
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
  closeLabel = "Close",
  style,
  testID,
  triggerLabel = "More",
}: SalesToolbarProps) {
  const [menuVisible, setMenuVisible] = useState(false);
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
  const initialVisibleOrder = useMemo(() => [...visibleIds], [visibleIds]);
  const [visibleOrder, setVisibleOrder] = useState(initialVisibleOrder);
  const visibleOrderRef =
    useRef<readonly SalesToolbarActionId[]>(initialVisibleOrder);
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
    [accessibilityCopy, actionMap, canonical, moveVisibleAction, onOrderChange],
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

  return (
    <View style={[styles.toolbarFrame, style]}>
      <HandheldActionButton
        label={triggerLabel}
        onPress={() => setMenuVisible(true)}
        sound="navigate"
        testID={testID ?? "sales-toolbar"}
        variant="secondary"
      />
      <Modal
        animationType="fade"
        onRequestClose={() => setMenuVisible(false)}
        statusBarTranslucent
        supportedOrientations={["portrait"]}
        transparent
        visible={menuVisible}
      >
        <View style={styles.modalBackdrop}>
          <HandheldStateSurface
            slug="sales-more-actions"
            style={styles.modalPanel}
          >
            <Text style={styles.modalTitle}>{triggerLabel}</Text>
            <ScrollView
              contentContainerStyle={styles.toolbarContent}
              scrollEnabled={draggingId === null}
              showsVerticalScrollIndicator={false}
              style={styles.toolbarViewport}
              testID="sales-toolbar-actions"
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
                      accessibilityLabel={
                        action.accessibilityLabel ?? action.label
                      }
                      accessibilityRole="button"
                      accessibilityState={{
                        disabled: action.disabled === true,
                      }}
                      cancelable={!isDragging}
                      delayLongPress={LONG_PRESS_DELAY_MS}
                      onAccessibilityAction={(event) => {
                        const direction =
                          event.nativeEvent.actionName === "move-earlier"
                            ? -1
                            : event.nativeEvent.actionName === "move-later"
                              ? 1
                              : null;
                        if (direction)
                          commitAccessibleMove(action.id, direction);
                      }}
                      onLongPress={() => beginDrag(action.id)}
                      onPress={() => {
                        const suppress = suppressBusinessPressRef.current;
                        suppressBusinessPressRef.current = false;
                        if (!suppress && !action.disabled) {
                          setMenuVisible(false);
                          action.onPress();
                        }
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
            <HandheldActionButton
              label={closeLabel}
              onPress={() => setMenuVisible(false)}
              testID="sales-toolbar-close"
              variant="secondary"
            />
          </HandheldStateSurface>
        </View>
      </Modal>
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
  return (
    first.length === second.length &&
    first.every((id, index) => id === second[index])
  );
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
  modalBackdrop: {
    alignItems: "center",
    backgroundColor: "rgba(16, 37, 58, 0.28)",
    flex: 1,
    justifyContent: "center",
    padding: 16,
  },
  modalPanel: {
    backgroundColor: posColors.canvas,
    borderColor: posColors.border,
    borderRadius: 6,
    borderWidth: 1,
    gap: 8,
    maxHeight: "86%",
    maxWidth: 520,
    padding: 16,
    width: "100%",
  },
  modalTitle: {
    color: posColors.ink,
    fontSize: 20,
    fontWeight: "800",
  },
  toolbarContent: {
    alignItems: "stretch",
    flexGrow: 1,
    flexDirection: "column",
    gap: 8,
  },
  toolbarFrame: {
    minHeight: MIN_TOUCH_TARGET,
  },
  toolbarViewport: {
    flexGrow: 0,
    width: "100%",
  },
});
