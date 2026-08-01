import { useMemo } from "react";
import { Alert, Linking, ScrollView, StyleSheet, View } from "react-native";
import { useRouter } from "expo-router";
import { Button, Divider, IconButton, Surface, Text } from "react-native-paper";
import { SafeAreaView } from "react-native-safe-area-context";
import { getMobilePrivacyPolicy } from "@/shared/legal/mobile-privacy-policy";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

const BRAND_RED = "#E53935";

export default function PrivacyScreen() {
  const router = useRouter();
  const { language } = useAppTranslation();
  const policy = useMemo(() => getMobilePrivacyPolicy(language), [language]);

  const goBack = () => {
    if (router.canGoBack()) {
      router.back();
      return;
    }
    router.replace("/(auth)/login");
  };

  const openLink = async (url: string) => {
    try {
      await Linking.openURL(url);
    } catch {
      Alert.alert(policy.footer.openFailedTitle, policy.footer.openFailedMessage);
    }
  };

  return (
    <SafeAreaView edges={["top", "left", "right", "bottom"]} style={styles.container}>
      <View style={styles.header}>
        <IconButton
          icon="arrow-left"
          accessibilityLabel={policy.footer.backLabel}
          onPress={goBack}
        />
        <Text variant="titleMedium" style={styles.headerTitle} numberOfLines={1}>
          {policy.title}
        </Text>
        <View style={styles.headerSpacer} />
      </View>

      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        <View style={styles.intro}>
          <Text variant="headlineSmall" style={styles.title}>
            {policy.title}
          </Text>
          <Text variant="bodyMedium" style={styles.subtitle}>
            {policy.subtitle}
          </Text>
          <Text variant="labelMedium" style={styles.effectiveDate}>
            {policy.effectiveDateLabel}: {policy.effectiveDate}
          </Text>
          <Text variant="bodyMedium" style={styles.summary}>
            {policy.summary}
          </Text>
        </View>

        <Surface style={styles.organizationCard} elevation={0}>
          <Text variant="labelMedium" style={styles.metaLabel}>
            {policy.organization.label}
          </Text>
          <Text variant="bodyMedium" style={styles.organizationName}>
            {policy.organization.name}
          </Text>
          <Text variant="labelMedium" style={styles.metaLabel}>
            {policy.organization.contactLabel}
          </Text>
          <Text
            variant="bodyMedium"
            style={styles.linkText}
            accessibilityRole="link"
            onPress={() => void openLink(`mailto:${policy.organization.email}`)}
          >
            {policy.organization.email}
          </Text>
        </Surface>

        <View style={styles.sections}>
          {policy.sections.map((section, sectionIndex) => (
            <View key={section.id} style={styles.section}>
              {sectionIndex > 0 ? <Divider style={styles.divider} /> : null}
              <Text variant="titleMedium" style={styles.sectionTitle}>
                {section.title}
              </Text>
              {section.paragraphs.map((paragraph) => (
                <Text key={paragraph} variant="bodyMedium" style={styles.paragraph}>
                  {paragraph}
                </Text>
              ))}
              {section.items.length ? (
                <View style={styles.list}>
                  {section.items.map((item) => (
                    <View key={item} style={styles.listRow}>
                      <Text style={styles.bullet}>•</Text>
                      <Text variant="bodyMedium" style={styles.listText}>
                        {item}
                      </Text>
                    </View>
                  ))}
                </View>
              ) : null}
            </View>
          ))}
        </View>

        <View style={styles.actions}>
          <Button
            mode="outlined"
            icon="open-in-new"
            onPress={() => void openLink(policy.footer.publicUrl)}
          >
            {policy.footer.publicCopy}
          </Button>
          <Button
            mode="text"
            icon="email-outline"
            textColor={BRAND_RED}
            onPress={() => void openLink(`mailto:${policy.organization.email}`)}
          >
            {policy.footer.emailLabel}
          </Button>
        </View>
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: "#F5F7FA" },
  header: {
    minHeight: 52,
    paddingHorizontal: 4,
    flexDirection: "row",
    alignItems: "center",
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: "#D9DDE5",
    backgroundColor: "#FFFFFF",
  },
  headerTitle: { flex: 1, textAlign: "center", fontWeight: "700" },
  headerSpacer: { width: 48 },
  content: { paddingHorizontal: 16, paddingTop: 20, paddingBottom: 32 },
  intro: { gap: 6 },
  title: { color: "#20242C", fontWeight: "800" },
  subtitle: { color: "#4B5563" },
  effectiveDate: { color: BRAND_RED, marginTop: 2 },
  summary: { color: "#303640", lineHeight: 22, marginTop: 8 },
  organizationCard: {
    marginTop: 18,
    padding: 14,
    borderRadius: 8,
    borderWidth: 1,
    borderColor: "#E1E5EB",
    backgroundColor: "#FFFFFF",
    gap: 3,
  },
  metaLabel: { color: "#6B7280", marginTop: 3 },
  organizationName: { color: "#20242C", fontWeight: "700" },
  linkText: { color: "#1467C9", textDecorationLine: "underline" },
  sections: { marginTop: 8 },
  section: { paddingTop: 12 },
  divider: { marginBottom: 18, backgroundColor: "#D9DDE5" },
  sectionTitle: { color: "#20242C", fontWeight: "700", marginBottom: 8 },
  paragraph: { color: "#394150", lineHeight: 22, marginBottom: 8 },
  list: { gap: 8, marginBottom: 8 },
  listRow: { flexDirection: "row", alignItems: "flex-start", paddingRight: 4 },
  bullet: { color: BRAND_RED, width: 18, lineHeight: 22, fontWeight: "800" },
  listText: { flex: 1, color: "#394150", lineHeight: 22 },
  actions: { gap: 4, marginTop: 18 },
});
