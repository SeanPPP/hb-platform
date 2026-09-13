import { useState } from "react";
import { FlatList, RefreshControl, View } from "react-native";
import { useLocalSearchParams } from "expo-router";
import { useQuery } from "@tanstack/react-query";
import { ActivityIndicator, IconButton, Text } from "react-native-paper";
import { fetchIdentityUserLoginRecords } from "./api";
import { AdminEmpty, AdminError, AdminScreen, styles } from "./ui";
import { identityDate, useIdentitySession, useIdentityUserCopy } from "./user-hooks";

export default function IdentityUserLoginRecordsScreen() {
  const { userGuid: param } = useLocalSearchParams<{ userGuid?: string | string[] }>();
  const userGuid = (Array.isArray(param) ? param[0] : param) ?? "";
  const { actorKey } = useIdentitySession();
  return <LoginRecords key={`${actorKey}:${userGuid}`} userGuid={userGuid} />;
}

function LoginRecords({ userGuid }: { userGuid: string }) {
  const c = useIdentityUserCopy();
  const { allowed, actorKey } = useIdentitySession();
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const records = useQuery({ queryKey: ["identity-admin", actorKey, "login-records", userGuid, page], queryFn: ({ signal }) => fetchIdentityUserLoginRecords(userGuid!, { page, pageSize }, actorKey, signal), enabled: allowed && !!userGuid, retry: false });
  if (!allowed) return <AdminScreen title={c.loginRecords}><AdminEmpty text={c.noAccess} /></AdminScreen>;
  return <AdminScreen title={c.loginRecords} action={<IconButton icon="refresh" accessibilityLabel={c.refresh} onPress={() => void records.refetch()} />}>
    {records.isError ? <AdminError error={records.error} onRetry={() => void records.refetch()} /> : null}
    {records.isPending ? <ActivityIndicator style={{ margin: 32 }} /> : <FlatList data={records.data?.items ?? []} keyExtractor={item => item.sessionId}
      refreshControl={<RefreshControl refreshing={records.isFetching} onRefresh={() => void records.refetch()} />} ListEmptyComponent={!records.isError ? <AdminEmpty text={c.noLogins} /> : null}
      renderItem={({ item }) => <View style={[styles.content, { borderBottomWidth: 1, borderBottomColor: "#E4E7EC", gap: 6 }]}>
        <Text style={styles.value}>{identityDate(item.loginAt)}</Text>
        <Text style={styles.label}>{item.status === "active" ? c.sessionActive : item.status === "revoked" ? c.sessionRevoked : c.sessionExpired} · {item.ipAddress || "—"}</Text>
        <Text style={styles.muted}>{c.expiresAt}: {identityDate(item.expiresAt)}</Text>
        <Text style={styles.muted}>{c.userAgent}: {item.userAgent || "—"}</Text>
      </View>} />}
    <View style={[styles.footer, { flexDirection: "row", alignItems: "center" }]}>
      <Text style={[styles.muted, { flex: 1 }]}>{page} / {Math.max(1, Math.ceil((records.data?.total ?? 0) / pageSize))}</Text>
      <IconButton icon="chevron-left" accessibilityLabel={c.previous} disabled={page <= 1 || records.isFetching} onPress={() => setPage(value => value - 1)} />
      <IconButton icon="chevron-right" accessibilityLabel={c.next} disabled={page * pageSize >= (records.data?.total ?? 0) || records.isFetching} onPress={() => setPage(value => value + 1)} />
    </View>
  </AdminScreen>;
}
