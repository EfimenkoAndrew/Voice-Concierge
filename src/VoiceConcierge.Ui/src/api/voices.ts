import { BASE, adminHeaders, json, ok } from "./client";

export interface Voice {
  id: number;
  name: string;
  description: string;
  providerVoiceId: string;
  active: boolean;
  isActive: boolean;
}

export interface ActiveVoice {
  voiceId: number;
  appliedAt: string;
}

export const listVoices = (includeDisabled = false) =>
  fetch(`${BASE}/voices?includeDisabled=${includeDisabled}`).then(json<Voice[]>);

export const setActiveVoice = (voiceId: number) =>
  ok(fetch(`${BASE}/config/voice`, { method: "PUT", headers: adminHeaders(), body: JSON.stringify({ voiceId }) }));

export const updateVoice = (
  id: number,
  b: { description: string; sampleText?: string; isActive: boolean },
) =>
  ok(fetch(`${BASE}/voices/${id}`, { method: "PUT", headers: adminHeaders(), body: JSON.stringify(b) }));

export const previewUrl = (id: number) => `${BASE}/voices/${id}/preview`;

export const previewWith = (id: number, text: string) =>
  fetch(`${BASE}/voices/${id}/preview`, {
    method: "POST",
    headers: adminHeaders(),
    body: JSON.stringify({ text }),
  });
