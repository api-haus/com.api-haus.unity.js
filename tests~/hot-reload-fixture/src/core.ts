// --- CONST_SLOT ---
export const VALUE = 1;
// --- COMMENT_SLOT ---
// touch 0

export function version(): number {
  return VALUE;
}

export function clamp(x: number, lo: number, hi: number): number {
  return Math.min(Math.max(x, lo), hi);
}

// Register in global version registry
(globalThis as any).__versions = (globalThis as any).__versions || {};
(globalThis as any).__versions["core"] = VALUE;
