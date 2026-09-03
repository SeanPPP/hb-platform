import { useLocalSearchParams } from "expo-router";
import { ContainerDetailScreen } from "@/modules/containers";

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value ?? "";
}

export default function ContainerDetailRoute() {
  const params = useLocalSearchParams();
  return <ContainerDetailScreen containerGuid={firstParam(params.containerGuid)} />;
}
