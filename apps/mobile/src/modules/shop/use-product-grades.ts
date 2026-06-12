import { useQuery } from "@tanstack/react-query";
import { getProductGradeOptions } from "@/modules/shop/api";

export function useProductGrades() {
  return useQuery({
    queryKey: ["shopProductGrades"],
    queryFn: getProductGradeOptions,
  });
}
