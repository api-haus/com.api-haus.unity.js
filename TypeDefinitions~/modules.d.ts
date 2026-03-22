// Module declarations for unity.js/* synthetic ES modules

declare module 'unity.js/types' {
  export function float2(x?: number | float2, y?: number): float2;
  export function float3(x?: number | float3, y?: number, z?: number): float3;
  export function float4(x?: number | float4, y?: number, z?: number, w?: number): float4;

  export namespace float2 {
    const zero: float2;
    const one: float2;
  }
  export namespace float3 {
    const zero: float3;
    const one: float3;
  }
  export namespace float4 {
    const zero: float4;
    const one: float4;
  }

  export function add<T extends float2 | float3 | float4>(a: T, b: T | number): T;
  export function add<T extends float2 | float3 | float4>(a: number, b: T): T;
  export function sub<T extends float2 | float3 | float4>(a: T, b: T | number): T;
  export function sub<T extends float2 | float3 | float4>(a: number, b: T): T;
  export function mul<T extends float2 | float3 | float4>(a: T, b: T | number): T;
  export function mul<T extends float2 | float3 | float4>(a: number, b: T): T;
  export function div<T extends float2 | float3 | float4>(a: T, b: T | number): T;
  export function div<T extends float2 | float3 | float4>(a: number, b: T): T;
  export function eq<T extends float2 | float3 | float4>(a: T, b: T | number): boolean;
  export function eq<T extends float2 | float3 | float4>(a: number, b: T): boolean;
}

declare module 'unity.js/ecs' {
  export function query(): QueryBuilder;
  export function define(componentName: string, schema?: object): void;
  export function add(entityId: entity, componentName: string, data?: object): object | true;
  export function add<T extends typeof Component>(entityId: entity, comp: T, data?: Partial<InstanceType<T>>): InstanceType<T>;
  export function remove(entityId: entity, componentName: string): void;
  export function has(entityId: entity, componentName: string): boolean;
  export function get<T>(accessor: ComponentAccessor<T>, entityId: entity): T | null;
  export function get<T>(accessor: ReadonlyComponentAccessor<T>, entityId: entity): Readonly<T> | null;
  export function get<T extends typeof Component>(accessor: T, entityId: entity): InstanceType<T> | undefined;

  export class Component {
    entity: entity;
    start?(): void;
    update?(dt?: number): void;
    fixedUpdate?(dt?: number): void;
    lateUpdate?(dt?: number): void;
    onDestroy?(): void;
    static get<T extends typeof Component>(this: T, eid: entity): InstanceType<T> | undefined;
  }
}

declare module 'unity.js/math' {
  // Constants
  export const PI: number;
  export const E: number;
  export const EPSILON: number;
  export const INFINITY: number;
  export function random(): number;

  // Componentwise unary
  export function sin<T extends number | float2 | float3 | float4>(x: T): T;
  export function cos<T extends number | float2 | float3 | float4>(x: T): T;
  export function tan<T extends number | float2 | float3 | float4>(x: T): T;
  export function asin<T extends number | float2 | float3 | float4>(x: T): T;
  export function acos<T extends number | float2 | float3 | float4>(x: T): T;
  export function atan<T extends number | float2 | float3 | float4>(x: T): T;
  export function sinh<T extends number | float2 | float3 | float4>(x: T): T;
  export function cosh<T extends number | float2 | float3 | float4>(x: T): T;
  export function tanh<T extends number | float2 | float3 | float4>(x: T): T;
  export function floor<T extends number | float2 | float3 | float4>(x: T): T;
  export function ceil<T extends number | float2 | float3 | float4>(x: T): T;
  export function round<T extends number | float2 | float3 | float4>(x: T): T;
  export function trunc<T extends number | float2 | float3 | float4>(x: T): T;
  export function frac<T extends number | float2 | float3 | float4>(x: T): T;
  export function sqrt<T extends number | float2 | float3 | float4>(x: T): T;
  export function rsqrt<T extends number | float2 | float3 | float4>(x: T): T;
  export function exp<T extends number | float2 | float3 | float4>(x: T): T;
  export function exp2<T extends number | float2 | float3 | float4>(x: T): T;
  export function log<T extends number | float2 | float3 | float4>(x: T): T;
  export function log2<T extends number | float2 | float3 | float4>(x: T): T;
  export function log10<T extends number | float2 | float3 | float4>(x: T): T;
  export function abs<T extends number | float2 | float3 | float4>(x: T): T;
  export function sign<T extends number | float2 | float3 | float4>(x: T): T;
  export function saturate<T extends number | float2 | float3 | float4>(x: T): T;
  export function radians<T extends number | float2 | float3 | float4>(x: T): T;
  export function degrees<T extends number | float2 | float3 | float4>(x: T): T;

  // Componentwise binary
  export function min<T extends number | float2 | float3 | float4>(a: T, b: T): T;
  export function max<T extends number | float2 | float3 | float4>(a: T, b: T): T;
  export function pow<T extends number | float2 | float3 | float4>(a: T, b: T): T;
  export function step<T extends number | float2 | float3 | float4>(a: T, b: T): T;

  // Interpolation
  export function lerp<T extends number | float2 | float3 | float4>(a: T, b: T, t: number): T;
  export function lerp<T extends number | float2 | float3 | float4>(a: T, b: T, t: T): T;
  export function clamp<T extends number | float2 | float3 | float4>(x: T, a: T, b: T): T;
  export function smoothstep<T extends number | float2 | float3 | float4>(a: T, b: T, x: T): T;

  // Scalar-only
  export function atan2(y: number, x: number): number;
  export function unlerp(a: number, b: number, x: number): number;
  export function remap(a: number, b: number, c: number, d: number, x: number): number;

  // Vector -> scalar
  export function dot<T extends float2 | float3 | float4>(a: T, b: T): number;
  export function length(v: float2 | float3 | float4): number;
  export function lengthsq(v: float2 | float3 | float4): number;
  export function distance<T extends float2 | float3 | float4>(a: T, b: T): number;
  export function distancesq<T extends float2 | float3 | float4>(a: T, b: T): number;

  // Vector -> vector
  export function normalize<T extends float2 | float3 | float4>(v: T): T;
  export function cross(a: float3, b: float3): float3;
  export function reflect<T extends float2 | float3 | float4>(i: T, n: T): T;
  export function refract<T extends float2 | float3 | float4>(i: T, n: T, eta: number): T;
}

declare module 'unity.js/input' {
  export function readValue(actionName: string): number | float2 | null;
  export function wasPressed(actionName: string): boolean;
  export function isHeld(actionName: string): boolean;
  export function wasReleased(actionName: string): boolean;
}

declare module 'unity.js/entities' {
  export function create(position?: float3 | number, y?: number, z?: number): entity;
  export function destroy(entityId: entity): boolean;
  export function addScript(entityId: entity, scriptName: string): boolean;
  export function hasScript(entityId: entity, scriptName: string): boolean;
  export function removeComponent(entityId: entity, componentName: string): boolean;
}

declare module 'unity.js/log' {
  export function debug(msg: unknown): void;
  export function info(msg: unknown): void;
  export function warning(msg: unknown): void;
  export function error(msg: unknown): void;
  export function trace(msg: unknown): void;
}

declare module 'unity.js/colors' {
  export function rgbToHsv(rgb: float3): { h: number; s: number; v: number };
  export function hsvToRgb(h: number, s: number, v: number): float3;
  export function oklabToRgb(lab: float3): float3;
  export function rgbToOklab(rgb: float3): float3;
}

declare module 'unity.js/draw' {
  export function setColor(r: number, g: number, b: number, a?: number): void;
  export function withDuration(duration: number): void;
  export function line(from: float3, to: float3): void;
  export function ray(origin: float3, direction: float3): void;
  export function arrow(from: float3, to: float3): void;
  export function wireSphere(center: float3, radius: number): void;
  export function wireBox(center: float3, size: float3): void;
  export function wireCapsule(start: float3, end: float3, radius: number): void;
  export function circleXz(center: float3, radius: number): void;
  export function solidBox(center: float3, size: float3): void;
  export function solidCircle(center: float3, normal: float3, radius: number): void;
  export function label2d(position: float3, text: string): void;
}

declare module 'unity.js/spatial' {
  export function add(eid: entity, tag: string, shape: SpatialShapeObj): boolean;
  export function get(eid: entity): SpatialShapeObj | undefined;
  export function query(tag: string, shape: SpatialShapeObj): entity[];
  export function sphere(radius: number, center?: float3): SpatialShapeObj;
  export function box(halfExtents: float3, center?: float3): SpatialShapeObj;

  export interface TriggerHandle {
    on(event: 'enter' | 'stay' | 'exit', callback: (other: entity) => void): TriggerHandle;
    destroy(): void;
  }
  export function trigger(eid: entity, tag: string, shape: SpatialShapeObj): TriggerHandle;
}

declare module 'unity.js/system' {
  export function deltaTime(): number;
  export function time(): number;
  export function random(min?: number, max?: number): number;
  export function randomInt(min?: number, max?: number): number;
}
