import { healthPct } from "./ai_state.js";
import { dampedSpring } from "./physics.js";

// --- CONST_SLOT ---
export const VALUE = 100000;
// --- COMMENT_SLOT ---
// touch 0

export function version(): number {
  return VALUE;
}

export function flee(hp: number, maxHp: number, speed: number, t: number): number {
  const urgency = 1 - healthPct(hp, maxHp);
  return dampedSpring(0, speed * urgency, t);
}

(globalThis as any).__versions = (globalThis as any).__versions || {};
(globalThis as any).__versions["ai_behavior"] = VALUE;
