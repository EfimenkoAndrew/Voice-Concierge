import "@testing-library/jest-dom/vitest";
import { vi } from "vitest";

// Pages fetch on mount — stub fetch so jsdom tests stay deterministic/quiet.
globalThis.fetch = vi.fn(async () =>
  new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } }),
) as unknown as typeof fetch;
