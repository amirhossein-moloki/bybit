"use client";

import {
  useQuery,
  type QueryKey,
  type UseQueryOptions,
} from "@tanstack/react-query";
import { useAuth } from "@/lib/auth";

export function useAuthedQuery<T>(
  queryKey: QueryKey,
  fetcher: (token: string) => Promise<T>,
  options?: Omit<UseQueryOptions<T>, "queryKey" | "queryFn" | "enabled">
) {
  const { token } = useAuth();

  return useQuery<T>({
    queryKey,
    queryFn: () => {
      if (!token) {
        throw new Error("Not authenticated");
      }
      return fetcher(token);
    },
    enabled: Boolean(token),
    ...options,
  });
}
