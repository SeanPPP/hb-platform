import { ScrollView, StyleSheet, useWindowDimensions, View } from "react-native";
import { ActivityIndicator, Button, Modal, Portal, RadioButton, Text } from "react-native-paper";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { HB_COLORS } from "@/shared/theme/tokens";

export interface SelectionListItem {
  key: string;
  label: string;
  description?: string | null;
}

interface SelectionListModalProps {
  presentation?: "dialog" | "sheet";
  visible: boolean;
  title: string;
  cancelLabel: string;
  items: SelectionListItem[];
  selectedKey?: string | null;
  includeAllOption?: boolean;
  allLabel?: string;
  loading?: boolean;
  emptyLabel: string;
  onDismiss: () => void;
  onSelect: (item: SelectionListItem | null) => void | Promise<void>;
}

export function SelectionListModal({
  presentation = "dialog",
  visible,
  title,
  cancelLabel,
  items,
  selectedKey,
  includeAllOption = false,
  allLabel,
  loading = false,
  emptyLabel,
  onDismiss,
  onSelect,
}: SelectionListModalProps) {
  const { height } = useWindowDimensions();
  const insets = useSafeAreaInsets();
  const isSheet = presentation === "sheet";
  const rowCount = items.length + (includeAllOption ? 1 : 0);
  const maxListHeight = Math.max(160, Math.min(480, height * 0.58));
  const listHeight = Math.min(Math.max(rowCount, 3) * 56, maxListHeight);

  return (
    <Portal>
      <Modal
        visible={visible}
        onDismiss={onDismiss}
        style={isSheet ? styles.sheetOverlay : undefined}
        contentContainerStyle={[styles.container, isSheet && styles.sheet, isSheet && { paddingBottom: Math.max(insets.bottom, 16) }]}
      >
        {isSheet ? <View style={styles.handle} /> : null}
        <Text variant="titleLarge" style={[styles.title, isSheet && styles.sheetTitle]}>
          {title}
        </Text>

        {loading ? (
          <View style={[styles.feedback, { height: Math.min(160, listHeight) }]}>
            <ActivityIndicator size="small" />
          </View>
        ) : items.length === 0 && !includeAllOption ? (
          <View style={[styles.feedback, { height: Math.min(160, listHeight) }]}>
            <Text variant="bodyMedium" style={styles.feedbackText}>
              {emptyLabel}
            </Text>
          </View>
        ) : (
          <ScrollView
            style={[styles.list, { height: listHeight }]}
            contentContainerStyle={styles.listContent}
            bounces={false}
            alwaysBounceVertical={false}
            keyboardShouldPersistTaps="handled"
            nestedScrollEnabled
            overScrollMode="never"
            showsVerticalScrollIndicator
          >
            {includeAllOption ? (
              <SelectionRow
                item={{
                  key: "__all__",
                  label: allLabel ?? title,
                }}
                selected={!selectedKey}
                onPress={() => void onSelect(null)}
              />
            ) : null}
            {items.map((item) => (
              <SelectionRow
                key={item.key}
                item={item}
                selected={item.key === selectedKey}
                onPress={() => void onSelect(item)}
              />
            ))}
          </ScrollView>
        )}

        <View style={styles.actions}>
          <Button contentStyle={isSheet ? { minHeight: 44 } : undefined} onPress={onDismiss}>{cancelLabel}</Button>
        </View>
      </Modal>
    </Portal>
  );
}

function SelectionRow({
  item,
  selected,
  onPress,
}: {
  item: SelectionListItem;
  selected: boolean;
  onPress: () => void;
}) {
  return (
    <View style={styles.item}>
      <View style={styles.itemRow}>
        <RadioButton
          value={item.key}
          status={selected ? "checked" : "unchecked"}
          onPress={onPress}
        />
        <Button
          mode="text"
          onPress={onPress}
          style={styles.itemButton}
          contentStyle={styles.itemButtonContent}
          labelStyle={styles.itemButtonLabel}
        >
          {item.label}
        </Button>
      </View>
      {item.description ? (
        <Text variant="bodySmall" style={styles.itemDescription}>
          {item.description}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  sheetOverlay: { justifyContent: "flex-end" },
  sheet: { width: "100%", maxWidth: 680, borderTopLeftRadius: 18, borderTopRightRadius: 18, borderBottomLeftRadius: 0, borderBottomRightRadius: 0, paddingHorizontal: 16, paddingTop: 10 },
  sheetTitle: { fontWeight: "700", color: HB_COLORS.textPrimary },
  handle: { width: 44, height: 4, borderRadius: 2, backgroundColor: HB_COLORS.outline, alignSelf: "center", marginBottom: 16 },
  actions: {
    alignItems: "flex-end",
    marginTop: 8,
  },
  container: {
    alignSelf: "center",
    backgroundColor: "#FFFFFF",
    borderRadius: 12,
    padding: 20,
    width: "88%",
  },
  feedback: {
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 12,
  },
  feedbackText: {
    color: "#666",
    textAlign: "center",
  },
  item: {
    minHeight: 48,
    paddingVertical: 4,
  },
  itemRow: {
    alignItems: "center",
    flexDirection: "row",
    gap: 4,
  },
  itemButton: {
    flex: 1,
  },
  itemButtonContent: {
    justifyContent: "flex-start",
    minHeight: 40,
  },
  itemButtonLabel: {
    marginHorizontal: 0,
    textAlign: "left",
  },
  itemDescription: {
    color: "#666",
    marginLeft: 52,
    paddingBottom: 4,
  },
  list: {
    flexGrow: 0,
  },
  listContent: {
    paddingBottom: 4,
  },
  title: {
    marginBottom: 12,
  },
});
