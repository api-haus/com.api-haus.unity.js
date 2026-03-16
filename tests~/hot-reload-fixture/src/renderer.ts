import { smoothStep } from "./transform.js";
import { clamp } from "./core.js";

// --- CONST_SLOT ---
export const VALUE = 1000000;
// --- COMMENT_SLOT ---
// touch 0

export function version(): number {
  return VALUE;
}

export function fadeAlpha(t: number): number {
  return smoothStep(0, 1, clamp(t, 0, 1));
}

(globalThis as any).__versions = (globalThis as any).__versions || {};
(globalThis as any).__versions["renderer"] = VALUE;
