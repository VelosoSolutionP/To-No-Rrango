"use client";

import { createContext, useContext } from "react";
import type { AdminHttp } from "@/lib/admin-api";

export interface AdminContextValue {
  http: AdminHttp | null;
  establishmentId: string;
}

export const AdminContext = createContext<AdminContextValue>({ http: null, establishmentId: "" });

export const useAdmin = () => useContext(AdminContext);
