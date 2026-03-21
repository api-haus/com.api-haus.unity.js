// @tick: variable
// Example: Character input system that reads ECSCharacterControl/Stats/State.
// In tests, input is injected via globalThis._testInput from C#.
// In production, replace globalThis._testInput reads with real input.* calls.
//
// Uses global bridge objects (ecs, math) registered by unity.js runtime.
const STAMINA_DRAIN = 20; // per second while sprinting
const STAMINA_REGEN = 10; // per second while not sprinting
let charQuery;
export function onUpdate(state) {
    charQuery ?? (charQuery = globalThis.ecs.query()
        .withAll('ECSCharacterControl', 'ECSCharacterStats', 'ECSCharacterState')
        .build());
    const dt = state.deltaTime;
    const ti = globalThis._testInput ?? {};
    const math = globalThis.math;
    for (const [eid, ctrl, stats, charSt] of charQuery) {
        // Build world-space move vector (XZ plane, Y=0)
        ctrl.moveVector = { x: ti.moveX ?? 0, y: 0, z: ti.moveZ ?? 0 };
        // Sprinting + stamina
        const sprinting = (ti.sprint ?? false) && stats.stamina > 0;
        if (sprinting) {
            stats.stamina = math.max(0, stats.stamina - STAMINA_DRAIN * dt);
        }
        else {
            stats.stamina = math.min(stats.maxStamina, stats.stamina + STAMINA_REGEN * dt);
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
