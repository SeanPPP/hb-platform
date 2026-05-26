import type { ReactNode } from "react";
import { Pressable, ScrollView, StyleSheet, useWindowDimensions, View } from "react-native";
import { Button, Modal, Portal, RadioButton, Text } from "react-native-paper";
import type { Store } from "@/modules/shop/types";

interface StorePickerModalProps {
  visible: boolean;
  stores: Store[];
  selectedStoreCode?: string | null;
  title: string;
  cancelLabel: string;
  includeAllOption?: boolean;
  allLabel?: string;
  renderAllLabel?: (label: string) => ReactNode;
  renderStoreLabel?: (store: Store) => ReactNode;
  onDismiss: () => void;
  onSelectStore: (store: Store | null) => void | Promise<void>;
}

export function StorePickerModal({
  visible,
  stores,
  selectedStoreCode,
  title,
  cancelLabel,
  includeAllOption = false,
  allLabel,
  renderAllLabel,
  renderStoreLabel,
  onDismiss,
  onSelectStore,
}: StorePickerModalProps) {
  const { height } = useWindowDimensions();
  const rowCount = stores.length + (includeAllOption ? 1 : 0);
  const maxListHeight = Math.max(160, Math.min(480, height * 0.58));
  const listHeight = Math.min(rowCount * 48, maxListHeight);

  return (
    <Portal>
      <Modal
        visible={visible}
        onDismiss={onDismiss}
        contentContainerStyle={styles.container}
      >
        <Text variant="titleLarge" style={styles.title}>
          {title}
        </Text>
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
            <PickerRow
              label={allLabel ?? title}
              content={renderAllLabel?.(allLabel ?? title)}
              selected={!selectedStoreCode}
              onPress={() => void onSelectStore(null)}
            />
          ) : null}
          {stores.map((store) => (
            <PickerRow
              key={store.storeCode}
              label={store.storeName || store.storeCode}
              content={renderStoreLabel?.(store)}
              selected={store.storeCode === selectedStoreCode}
              onPress={() => void onSelectStore(store)}
            />
          ))}
        </ScrollView>
        <View style={styles.actions}>
          <Button onPress={onDismiss}>{cancelLabel}</Button>
        </View>
      </Modal>
    </Portal>
  );
}

function PickerRow({
  label,
  content,
  selected,
  onPress,
}: {
  label: string;
  content?: ReactNode;
  selected: boolean;
  onPress: () => void;
}) {
  return (
    <View style={styles.item}>
      <RadioButton
        value={label}
        status={selected ? "checked" : "unchecked"}
        onPress={onPress}
      />
      {content ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={label}
          onPress={onPress}
          style={styles.itemButton}
        >
          {content}
        </Pressable>
      ) : (
        <Button
          mode="text"
          onPress={onPress}
          style={styles.itemButton}
          contentStyle={styles.itemButtonContent}
        >
          {label}
        </Button>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
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
  item: {
    alignItems: "center",
    flexDirection: "row",
    gap: 4,
    minHeight: 48,
    paddingVertical: 2,
  },
  itemButton: {
    flex: 1,
    minWidth: 0,
  },
  itemButtonContent: {
    justifyContent: "flex-start",
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
