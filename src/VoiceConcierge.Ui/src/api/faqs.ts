import { BASE, adminHeaders, json, ok, type PagedResult } from "./client";

export interface Faq {
  id: string;
  question: string;
  answer: string;
  tags: string[];
  updatedAt: string;
}

export interface UpsertFaq {
  question: string;
  answer: string;
  tags?: string[] | null;
  updateTags?: boolean;
}

export const listFaqs = (after?: string, limit = 50) =>
  fetch(`${BASE}/faqs?limit=${limit}${after ? `&after=${after}` : ""}`)
    .then(json<PagedResult<Faq>>);

export const createFaq = (b: { question: string; answer: string; tags: string[] }) =>
  fetch(`${BASE}/faqs`, { method: "POST", headers: adminHeaders(), body: JSON.stringify(b) })
    .then(json<Faq>);

export const updateFaq = (id: string, b: UpsertFaq) =>
  ok(fetch(`${BASE}/faqs/${id}`, { method: "PUT", headers: adminHeaders(), body: JSON.stringify(b) }));

export const deleteFaq = (id: string) =>
  ok(fetch(`${BASE}/faqs/${id}`, { method: "DELETE", headers: adminHeaders() }));
