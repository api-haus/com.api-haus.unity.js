import { lerp } from "./math_utils.js";

// --- CONST_SLOT ---
export const VALUE = 100;
// --- COMMENT_SLOT ---
// touch 0

export function version(): number {
  return VALUE;
}

export function smoothStep(from: number, to: number, t: number): number {
  const s = t * t * (3 - 2 * t);
  return lerp(from, to, s);
}

(globalThis as any).__versions = (globalThis as any).__versions || {};
(globalThis as any).__versions["transform"] = VALUE;
