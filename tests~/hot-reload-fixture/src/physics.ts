import { smoothStep } from "./transform.js";
import { clamp } from "./core.js";

// --- CONST_SLOT ---
export const VALUE = 1000;
// --- COMMENT_SLOT ---
// touch 0

export function version(): number {
  return VALUE;
}

export function dampedSpring(current: number, target: number, t: number): number {
  return smoothStep(current, target, clamp(t, 0, 1));
}

(globalThis as any).__versions = (globalThis as any).__versions || {};
(globalThis as any).__versions["physics"] = VALUE;
