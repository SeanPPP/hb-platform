import { useState } from "react";
import { FlatList, View } from "react-native";
import { Button, Modal, Portal, RadioButton, Text, TouchableRipple } from "react-native-paper";
import { AdminEmpty, SearchField, styles } from "./ui";
import { useIdentityUserCopy } from "./user-hooks";

export function IdentitySelectionSheet({ title, value, options, onSelect, onDismiss }: {
  title: string; value?: string; options: { value: string; label: string }[]; onSelect: (value: string) => void; onDismiss: () => void;
}) {
  const c = useIdentityUserCopy();
  const [search, setSearch] = useState("");
  const filtered = options.filter(option => option.label.toLowerCase().includes(search.toLowerCase()));
  return <Portal><Modal visible onDismiss={onDismiss} contentContainerStyle={{ margin: 16, padding: 16, backgroundColor: "white", borderRadius: 16, maxHeight: "85%" }}>
    <Text style={styles.value}>{title}</Text>
    <View style={{ marginVertical: 12 }}><SearchField value={search} onChange={setSearch} placeholder={c.filterSearch} /></View>
    <FlatList data={filtered} keyExtractor={option => option.value} keyboardShouldPersistTaps="handled"
      ListEmptyComponent={<AdminEmpty text={c.noOptions} />}
      renderItem={({ item }) => <TouchableRipple onPress={() => onSelect(item.value)} accessibilityRole="radio" accessibilityState={{ checked: value === item.value }}>
        <View style={styles.row}><Text style={{ flex: 1 }}>{item.label}</Text><RadioButton value={item.value} status={value === item.value ? "checked" : "unchecked"} onPress={() => onSelect(item.value)} /></View>
      </TouchableRipple>} />
    <Button onPress={onDismiss}>{c.close}</Button>
  </Modal></Portal>;
}
