## [1.2.1](https://github.com/api-haus/com.api-haus.unity.js/compare/v1.2.0...v1.2.1) (2026-03-27)


### Performance Improvements

* eliminate ~10ms/frame of wasted JS tick system overhead ([6fc5685](https://github.com/api-haus/com.api-haus.unity.js/commit/6fc5685e109242bc503491b8e88da12af5dde528))

# [1.2.0](https://github.com/api-haus/com.api-haus.unity.js/compare/v1.1.0...v1.2.0) (2026-03-26)


### Bug Fixes

* centralize runtime resource lifecycle — eliminate TDZ regression ([c8d51f8](https://github.com/api-haus/com.api-haus.unity.js/commit/c8d51f8795e2114c18afe81c55624e45470bf632))
* dispose stale VM on system start to prevent TDZ after domain reload ([40ab44b](https://github.com/api-haus/com.api-haus.unity.js/commit/40ab44b9004ad4933f201a167c4f3a753384e552))
* eliminate TDZ errors in system probes, add PlayModeControlE2ETests ([bb37361](https://github.com/api-haus/com.api-haus.unity.js/commit/bb37361e007cf4cb4e96a2ee4e65eeeaa26f5579))
* remove auto-discovered query probe that broke PlayMode tests ([21d6f2b](https://github.com/api-haus/com.api-haus.unity.js/commit/21d6f2b64a2492d3eefbb6b64b70d790883c3898))
* resolve 10 failing hotreload tests ([dcdd80b](https://github.com/api-haus/com.api-haus.unity.js/commit/dcdd80bb56b00326323680f2113b1f18748ab7a2))
* resolve all 271 PlayMode test cross-contamination failures ([7e2ff9c](https://github.com/api-haus/com.api-haus.unity.js/commit/7e2ff9cb1a515061eb02b8fef7fa63d7dcd3cd16))
* strengthen weak assertions, fix e2e_entity_ops TDZ ([5cc108c](https://github.com/api-haus/com.api-haus.unity.js/commit/5cc108c0cd5f591321fc780c403d1703ad7b5e1d))
* suppress transient tick errors during live baking rebake ([9177c86](https://github.com/api-haus/com.api-haus.unity.js/commit/9177c8628eba0ab9e70788dad5ca481b9349b347))
* TDZ + spatial trigger — module dedup and JS_CFUNC_generic_magic enum ([e07ea11](https://github.com/api-haus/com.api-haus.unity.js/commit/e07ea11891fd69221f0520bb008540623f1a21d9))
* withNone query filter — JS_IsArray returns 0 for P/Invoke-returned arrays ([02722c4](https://github.com/api-haus/com.api-haus.unity.js/commit/02722c472e2f0631250055dd3b46a81daeea9d88))


### Features

* add ColorBridge + SystemInfo E2E tests (7 tests) ([70e65fc](https://github.com/api-haus/com.api-haus.unity.js/commit/70e65fce80be77d3d751191dcad107e1e2132060))
* add ComponentAccess E2E tests (define, add, get, has, remove) ([8acdaec](https://github.com/api-haus/com.api-haus.unity.js/commit/8acdaec795b671fc6678ec5dd5d9e7d9295df42f))
* add E2E tests for TDZ bug with unity.js/components import after domain reload ([168e621](https://github.com/api-haus/com.api-haus.unity.js/commit/168e621ab2a9aa644763f99385525bbeeb8dece0))
* add EntityOperations E2E tests (create, create with position, destroy) ([a3742ad](https://github.com/api-haus/com.api-haus.unity.js/commit/a3742ad1fa7e65038a1f4382286dd22084fe6b24))
* add hot reload resilience E2E tests ([9431b59](https://github.com/api-haus/com.api-haus.unity.js/commit/9431b59b756ecfcd6e73ec5a96cde602506ba7de))
* add HotReload E2E tests (version mutation + rapid reload stability) ([7ef9469](https://github.com/api-haus/com.api-haus.unity.js/commit/7ef946902e7c3aa9450bfe0d66907f013242abf0))
* add integration E2E tests for ALINE draw + spatial.query() ([ad1d86e](https://github.com/api-haus/com.api-haus.unity.js/commit/ad1d86e104f218991e387d74c2af2e21461a3b8e))
* add QueryPipeline E2E tests (match count, write-back, stability) ([b7300af](https://github.com/api-haus/com.api-haus.unity.js/commit/b7300afdcf5b1c6d95179f1e936117a4f42a413c))
* add runsAfter/runsBefore component execution ordering with topological sort ([9cc3950](https://github.com/api-haus/com.api-haus.unity.js/commit/9cc39505a521764c93955cfde6b61dbfd5030589))
* add SystemExecution and MathBridge E2E tests, fix spatial wait budget ([e654d40](https://github.com/api-haus/com.api-haus.unity.js/commit/e654d405636a03eb99fd78904f0618744dbe0bb5))
* address blind spots — property overrides test, tightened assertions, state isolation ([cd13853](https://github.com/api-haus/com.api-haus.unity.js/commit/cd13853103ce5c40bffd584dec06b78bd2759558))
* complete E2E test suite — LogBridge, TickGroups, MultiScript, ModuleImport, InputBridge, DomainReload ([1d4084a](https://github.com/api-haus/com.api-haus.unity.js/commit/1d4084a6707c51f27ee1f3a56fc339f5b24436ce))
* consolidate MiniSpatial runtime + JS bridge as bundled integration ([20c9ea3](https://github.com/api-haus/com.api-haus.unity.js/commit/20c9ea3a865bd18fc99a431f0acbe61f99f3486c))
* E2E test infrastructure with SceneFixture DSL and gameplay lighthouse tests ([da145fc](https://github.com/api-haus/com.api-haus.unity.js/commit/da145fc7796bed24a030d9af89d3f3214d504fcc))
* param() accepts explicit default value as required 3rd argument ([b502979](https://github.com/api-haus/com.api-haus.unity.js/commit/b5029790563e6e8a356edc072b10645a4ed86c3a))
* replace tsc build pipeline with runtime Sucrase transpilation ([1ed374d](https://github.com/api-haus/com.api-haus.unity.js/commit/1ed374d50e606036abd04c44e02b23ae5e11c739))
* StoredPrefs store, param() command system, and scripts/ script type ([4c00d51](https://github.com/api-haus/com.api-haus.unity.js/commit/4c00d5137394a957f52c03e710aa78eb6dd9fa21))
* use TypeScript logo for status bar indicator, neutral idle state ([c04c8cb](https://github.com/api-haus/com.api-haus.unity.js/commit/c04c8cb1edf98c2dc956e6e2cc8df87fcf71ca7c))


### Performance Improvements

* remove redundant ECS sync points in JS tick systems ([7cfcf15](https://github.com/api-haus/com.api-haus.unity.js/commit/7cfcf15a04b4946373add108da747c5d1e79a239))

# [1.1.0](https://github.com/api-haus/com.api-haus.unity.js/compare/v1.0.4...v1.1.0) (2026-03-22)


### Features

* isolated search paths for fixture tests, force system rediscovery ([58a5ad6](https://github.com/api-haus/com.api-haus.unity.js/commit/58a5ad6042452ee0837bdaaad897d599b1a2bbb2))

## [1.0.4](https://github.com/api-haus/com.api-haus.unity.js/compare/v1.0.3...v1.0.4) (2026-03-22)


### Bug Fixes

* pull --rebase before pushing binaries in CI ([2134e3a](https://github.com/api-haus/com.api-haus.unity.js/commit/2134e3a5be0ec16ead4b98f654a024681c2d7d3b))

## [1.0.3](https://github.com/api-haus/com.api-haus.unity.js/compare/v1.0.2...v1.0.3) (2026-03-22)


### Bug Fixes

* patch x87 FPU guard for android-x86 build ([c5604e3](https://github.com/api-haus/com.api-haus.unity.js/commit/c5604e36707cacc48798434155e3f8f9e4469b4b))

## [1.0.2](https://github.com/api-haus/com.api-haus.unity.js/compare/v1.0.1...v1.0.2) (2026-03-22)


### Bug Fixes

* windows and android-x86 quickjs-ng build failures ([24d405a](https://github.com/api-haus/com.api-haus.unity.js/commit/24d405a9286b0342fb89bb08a0ffb5c35854deff))

## [1.0.1](https://github.com/api-haus/com.api-haus.unity.js/compare/v1.0.0...v1.0.1) (2026-03-21)


### Bug Fixes

* also gitignore csc.rsp.meta files in Integrations ([43f8155](https://github.com/api-haus/com.api-haus.unity.js/commit/43f81559a6bd9722c6109c6aa31afafc2bbf3fb5))

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
* restrict JsComponentAuthoring inspector to public fields only ([aff6a58](https://github.com/api-haus/unity.js/commit/aff6a585e40c93014dcae3732c75a8b3cc2119a5))
* Stage 3 — ECS core (UnityJS.Entities assembly) ([a1c1674](https://github.com/api-haus/unity.js/commit/a1c1674a4f7401e814e040742490d5645df728b1))
* Stage 4 — fill 47 bridge function stubs, add EditMode test suite ([190a491](https://github.com/api-haus/unity.js/commit/190a4916cfffbbaada698d1c7bb1978073dad989))
* Stage 5-7,9 — remove legacy Lua, port editor tools to JS, enable codegen ([5e69b21](https://github.com/api-haus/unity.js/commit/5e69b21541382914afca0862170ecda8d24db5e9))
* tsc --watch status bar indicator in Unity Editor ([d9a4c6e](https://github.com/api-haus/unity.js/commit/d9a4c6e6ff30a5498a3e7b1bc7f5005d5083f1aa))
* vector arithmetic methods + fix Signature-mode JsCompile registration ([44b7bf1](https://github.com/api-haus/unity.js/commit/44b7bf1f4335f0260b9dc1bcada3fdb29d48741d))
