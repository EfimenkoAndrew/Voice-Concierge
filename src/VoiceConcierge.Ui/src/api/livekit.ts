import { BASE, jsonHeaders, json } from "./client";

export interface LiveKitConfig {
  room: string;
}

export interface LiveKitToken {
  token: string;
  url: string;
}

export const getLiveKitConfig = () =>
  fetch(`${BASE}/livekit/config`).then(json<LiveKitConfig>);

export const getLiveKitToken = (room: string, identity: string) =>
  fetch(`${BASE}/livekit/token`, {
    method: "POST",
    headers: jsonHeaders,
    body: JSON.stringify({ room, identity }),
  }).then(json<LiveKitToken>);
