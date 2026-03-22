// @tick: variable
import { ECSCharacterControl, ECSCharacterStats, ECSCharacterState } from 'unity.js/components';
import { query } from 'unity.js/ecs';
import { max, min } from 'unity.js/math';
import { float3 } from 'unity.js/types';

const STAMINA_DRAIN = 20; // per second while sprinting
const STAMINA_REGEN = 10; // per second while not sprinting

interface TestInput {
  moveX?: number;
  moveZ?: number;
  sprint?: boolean;
  jump?: boolean;
}

const charQuery = query()
  .withAll(ECSCharacterControl, ECSCharacterStats, ECSCharacterState)
  .build();

export function onTick(state: UpdateState): void {
  const dt = state.deltaTime;
  const ti: TestInput = (globalThis as { _testInput?: TestInput })._testInput ?? {};

  for (const [eid, ctrl, stats, charSt] of charQuery) {
    // Build world-space move vector (XZ plane, Y=0)
    ctrl.moveVector = float3(ti.moveX ?? 0, 0, ti.moveZ ?? 0);

    // Sprinting + stamina
    const sprinting = (ti.sprint ?? false) && stats.stamina > 0;
    if (sprinting) {
      stats.stamina = max(0, stats.stamina - STAMINA_DRAIN * dt);
    } else {
      stats.stamina = min(stats.maxStamina, stats.stamina + STAMINA_REGEN * dt);
    }
    ctrl.sprint = sprinting;

    // Reset jump count on landing
    if (charSt.isGrounded && !charSt.wasGroundedLastFrame) {
      stats.jumpCount = 0;
    }

    // Jumping with multi-jump support
    if ((ti.jump ?? false) && stats.jumpCount < stats.maxJumps) {
      ctrl.jump = true;
      stats.jumpCount = stats.jumpCount + 1;
    }
  }
}
