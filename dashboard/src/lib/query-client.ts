"use client";

import { QueryClient } from "@tanstack/react-query";
import { ApiError } from "./api-client";
import { notifyUnauthorized } from "./auth";

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: (failureCount, error) => {
          if (error instanceof ApiError && error.status === 401) {
            notifyUnauthorized();
            return false;
          }
          if (error instanceof ApiError && error.status === 403) {
            return false;
          }
          if (error instanceof ApiError && error.status === 404) {
            return false;
          }
          return failureCount < 2;
        },
        refetchOnWindowFocus: false,
        staleTime: 5_000,
        gcTime: 10 * 60 * 1000,
      },
      mutations: {
        retry: (failureCount, error) => {
          if (error instanceof ApiError && error.status === 401) {
            notifyUnauthorized();
            return false;
          }
          return false;
        },
      },
    },
  });
}
