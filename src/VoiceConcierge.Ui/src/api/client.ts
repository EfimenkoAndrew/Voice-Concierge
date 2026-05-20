import { getAdminKey } from "../store/adminKey";

export const BASE = (import.meta.env.VITE_API_BASE as string | undefined) ?? "http://localhost:8080";

export interface PagedResult<T> { items: T[]; nextCursor: string | null; }

export async function json<T>(r: Response): Promise<T> {
  if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
  return (await r.json()) as T;
}

export async function ok(p: Promise<Response>): Promise<void> {
  const r = await p;
  if (!r.ok) throw new Error(`${r.status} ${r.statusText}`);
}

export const jsonHeaders = { "Content-Type": "application/json" } as const;

export const adminHeaders = (): Record<string, string> => {
  const k = getAdminKey();
  return k
    ? { "Content-Type": "application/json", "X-Admin-Key": k }
    : { "Content-Type": "application/json" };
};
