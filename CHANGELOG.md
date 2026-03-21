# 1.0.0 (2026-03-21)


### Bug Fixes

* duplicate module guard + TscWatchService error reporting ([9e3cfb7](https://github.com/api-haus/unity.js/commit/9e3cfb7a38734cf95c0d98b6d468fe8f9bf80ee9))
* ecs.get() auto-flush deduplication — prevent stale entries from clobbering writes ([f729bb2](https://github.com/api-haus/unity.js/commit/f729bb2b46dbfe4e19f823ba6bddf13dd341730d))
* gitignore integration csc.rsp files so they ship disabled ([bb71561](https://github.com/api-haus/unity.js/commit/bb71561470999d8b45bf8bdf9c21784fbfb056bd))
* hot-reload HasScript guard, module health + exception capture API ([d3add5f](https://github.com/api-haus/unity.js/commit/d3add5fa59cd45f904958fb38ddffc57b2c75e90))
* null byte included in module loader length causing SyntaxError ([823d7be](https://github.com/api-haus/unity.js/commit/823d7be28c0b5cb84de70c03a2150ed3627109ca))
* null-terminate synthetic module source for JS_Eval ([8c3af46](https://github.com/api-haus/unity.js/commit/8c3af4697c10a02248a7bae592299d10830474f8))
* register bridges before script evaluation to prevent TDZ and silent failures ([1af5f3e](https://github.com/api-haus/unity.js/commit/1af5f3e3bdd3a311af5e13a7971320f390a46184))
* reset QJS shim callback table on VM recreation to prevent slot overflow ([259db58](https://github.com/api-haus/unity.js/commit/259db5814434bb3ea3f601ff8d7bd9dc09a0d5e1))
* start-only Components never ticked + late-bind spatial.trigger export ([26e2d3a](https://github.com/api-haus/unity.js/commit/26e2d3a9e24adde52ea5922d8a988499cd823ba5))


### Features

* add burst context and dependency tracking to JsTickSystemBase ([dda7f72](https://github.com/api-haus/unity.js/commit/dda7f72b87dfac5fa13f4d78acf8dbf13f5e2ff8))
* add C shim trampoline for managed QuickJS callbacks ([132769b](https://github.com/api-haus/unity.js/commit/132769be60910b5466e8cc71c9087449171a0626))
* add JsBridge.Marshal<T> API for JS→struct marshaling ([33ac178](https://github.com/api-haus/unity.js/commit/33ac178722bba51acfd998a500e9fee160d20240))
* add JsRuntime layer (Stage 2) ([3c7ef65](https://github.com/api-haus/unity.js/commit/3c7ef65b3a9b31f3d064431b334644fb4b4c5682))
* add RefRW auto-flush for ecs.get() component accessors ([d67b26e](https://github.com/api-haus/unity.js/commit/d67b26edee4f67c4cbf6509a947e39f6f5264ca9))
* auto write-back for ECS query iterator ([29d160d](https://github.com/api-haus/unity.js/commit/29d160d388f8b735fb9cda946d95e1653d8e132d))
* codegen compile-time bridge registration, vector math compiled helpers ([e1eda67](https://github.com/api-haus/unity.js/commit/e1eda67bf314eb537f6d404b4be00b1ee989e0f6))
* ES module layer for unity.js/* synthetic imports ([d392788](https://github.com/api-haus/unity.js/commit/d392788094bfb42737034249970f071998fef701))
* fix JsCompile codegen, expand type support, full math.* bridge ([1425d3e](https://github.com/api-haus/unity.js/commit/1425d3e29870ffa8c81f1d585340d9d6ea4d6cb4))
* float2/float4 overloads for vector math bridge functions ([473800c](https://github.com/api-haus/unity.js/commit/473800c1c187622e3a7249f424e9f2ac26d25bce))
* hot-reload rework — versioned module cache, tsc --watch, E2E stress tests ([a43ec59](https://github.com/api-haus/unity.js/commit/a43ec59c4491786f8a60493ad41ae6329d094ba5))
* hot-reload stress tests with synchronous TscCompiler ([8fa07bc](https://github.com/api-haus/unity.js/commit/8fa07bcdd59c20632cd2d0e93c7bb51ac2d3d65c))
* integration architecture with GUID-based detection, test assemblies, and reusable E2E harness ([4c0cdcb](https://github.com/api-haus/unity.js/commit/4c0cdcb236c4fd42f1811a752deb4eaa4d1512f9))
* JS ECS benchmark suite, expose _nativeQuery globally ([c7f8ba3](https://github.com/api-haus/unity.js/commit/c7f8ba3943d6d98ff711ab21a95badc163e2662d))
* refactor benchmarks to read-write only, codegen query bridge improvements ([b493d0b](https://github.com/api-haus/unity.js/commit/b493d0b18c94205301bf2ff3b8fb51c06c4e6b23))
* replace LuaJIT with QuickJS-ng P/Invoke bindings ([7caab57](https://github.com/api-haus/unity.js/commit/7caab57c65289af00ec745acae1a3e57c7c31e8b))
* restrict JsScriptAuthoring inspector to public fields only ([aff6a58](https://github.com/api-haus/unity.js/commit/aff6a585e40c93014dcae3732c75a8b3cc2119a5))
* Stage 3 — ECS core (UnityJS.Entities assembly) ([a1c1674](https://github.com/api-haus/unity.js/commit/a1c1674a4f7401e814e040742490d5645df728b1))
* Stage 4 — fill 47 bridge function stubs, add EditMode test suite ([190a491](https://github.com/api-haus/unity.js/commit/190a4916cfffbbaada698d1c7bb1978073dad989))
* Stage 5-7,9 — remove legacy Lua, port editor tools to JS, enable codegen ([5e69b21](https://github.com/api-haus/unity.js/commit/5e69b21541382914afca0862170ecda8d24db5e9))
* tsc --watch status bar indicator in Unity Editor ([d9a4c6e](https://github.com/api-haus/unity.js/commit/d9a4c6e6ff30a5498a3e7b1bc7f5005d5083f1aa))
* vector arithmetic methods + fix Signature-mode JsCompile registration ([44b7bf1](https://github.com/api-haus/unity.js/commit/44b7bf1f4335f0260b9dc1bcada3fdb29d48741d))
