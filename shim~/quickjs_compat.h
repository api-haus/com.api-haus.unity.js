/* Minimal QuickJS declarations for shim compilation.
   Must match the qjs.so binary (bellard/quickjs pre-2024 API). */
#ifndef QUICKJS_COMPAT_H
#define QUICKJS_COMPAT_H

#include <stdint.h>
#include <stddef.h>

typedef struct JSRuntime JSRuntime;
typedef struct JSContext JSContext;
typedef struct JSModuleDef JSModuleDef;

typedef union JSValueUnion {
    int32_t int32;
    double float64;
    void *ptr;
} JSValueUnion;

typedef struct JSValue {
    JSValueUnion u;
    int64_t tag;
} JSValue;

typedef JSValue JSValueConst;

#define JS_UNDEFINED ((JSValue){ .u.int32 = 0, .tag = 3 })
#define JS_TAG_EXCEPTION 6

typedef void *JSCFunction;

/* Subset of QuickJS C API used by the shim */
JSValue JS_NewCFunction2(JSContext *ctx, JSCFunction *func, const char *name,
                         int length, int cproto, int magic);
void JS_FreeValue(JSContext *ctx, JSValue val);
void *js_malloc(JSContext *ctx, size_t size);
void js_free(JSContext *ctx, void *ptr);
JSValue JS_Eval(JSContext *ctx, const char *input, size_t input_len,
                const char *filename, int eval_flags);
int JS_IsException(JSValueConst val);
JSValue JS_ThrowInternalError(JSContext *ctx, const char *fmt, ...);
void *JS_VALUE_GET_PTR(JSValue val);
int JS_ResolveModule(JSContext *ctx, JSValueConst val);
JSValue JS_EvalFunction(JSContext *ctx, JSValue fun);
JSValue JS_GetModuleNamespace(JSContext *ctx, JSModuleDef *m);
JSRuntime *JS_GetRuntime(JSContext *ctx);
void JS_SetModuleLoaderFunc(JSRuntime *rt, void *normalize, void *loader, void *opaque);
JSValue JS_NewArrayBufferCopy(JSContext *ctx, const uint8_t *buf, size_t len);
JSValue JS_NewTypedArray(JSContext *ctx, int argc, JSValueConst *argv, int type);

/* Property access */
JSValue JS_GetPropertyStr(JSContext *ctx, JSValueConst this_obj, const char *prop);
int JS_IsArray(JSContext *ctx, JSValueConst val);

/* Constants */
#define JS_CFUNC_generic_magic 5
#define JS_EVAL_TYPE_MODULE 1
#define JS_EVAL_FLAG_COMPILE_ONLY (1 << 5)
#define JS_TYPED_ARRAY_FLOAT32 10
#define JS_TYPED_ARRAY_INT32 8

#endif
