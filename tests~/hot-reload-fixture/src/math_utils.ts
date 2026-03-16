import { clamp } from "./core.js";

// --- CONST_SLOT ---
export const VALUE = 10;
// --- COMMENT_SLOT ---
// touch 0

export function version(): number {
  return VALUE;
}

export function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * clamp(t, 0, 1);
}

(globalThis as any).__versions = (globalThis as any).__versions || {};
(globalThis as any).__versions["math_utils"] = VALUE;
