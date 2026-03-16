import { clamp } from "./core.js";
import { lerp } from "./math_utils.js";

// --- CONST_SLOT ---
export const VALUE = 10000;
// --- COMMENT_SLOT ---
// touch 0

export function version(): number {
  return VALUE;
}

export function healthPct(current: number, max: number): number {
  return clamp(current / max, 0, 1);
}

export function blendPriority(a: number, b: number, urgency: number): number {
  return lerp(a, b, clamp(urgency, 0, 1));
}

(globalThis as any).__versions = (globalThis as any).__versions || {};
(globalThis as any).__versions["ai_state"] = VALUE;
