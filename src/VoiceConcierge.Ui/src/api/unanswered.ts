import { BASE, adminHeaders, json, ok, type PagedResult } from "./client";

export type UnansweredStatus = "Open" | "Converted" | "Dismissed";

export interface Unanswered {
  id: string;
  question: string;
  frequency: number;
  firstAskedAt: string;
  lastAskedAt: string;
  status: string;
}

export const listUnanswered = (status = "Open", after?: string, limit = 50) =>
  fetch(`${BASE}/unanswered?status=${status}&limit=${limit}${after ? `&after=${after}` : ""}`)
    .then(json<PagedResult<Unanswered>>);

export const convertUnanswered = (id: string, b: { answer: string; tags: string[] }) =>
  ok(fetch(`${BASE}/unanswered/${id}/convert`, { method: "POST", headers: adminHeaders(), body: JSON.stringify(b) }));

export const dismissUnanswered = (id: string) =>
  ok(fetch(`${BASE}/unanswered/${id}/dismiss`, { method: "POST", headers: adminHeaders() }));
