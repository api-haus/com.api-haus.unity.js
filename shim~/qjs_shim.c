#ifdef USE_COMPAT_HEADER
#include "quickjs_compat.h"
#else
#include "quickjs.h"
#endif
#include <stdint.h>
#include <string.h>

#ifdef _WIN32
#define SHIM_API __declspec(dllexport)
#else
#define SHIM_API __attribute__((visibility("default")))
#endif

#define MAX_CALLBACKS 256

typedef void (*ManagedCallback)(
    void *ctx,
    int64_t this_u,
    int64_t this_tag,
    int argc,
    void *argv,
    int64_t *out_u,
    int64_t *out_tag
);

static ManagedCallback s_callbacks[MAX_CALLBACKS];
static int s_count = 0;

static JSValue trampoline(JSContext *ctx, JSValueConst this_val,
                          int argc, JSValueConst *argv, int magic)
{
    JSValue result;
    int64_t *pu = (int64_t *)&result;
    int64_t *ptag = pu + 1;
    s_callbacks[magic](ctx, *(int64_t *)&this_val, *((int64_t *)&this_val + 1),
                       argc, (void *)argv, pu, ptag);
    return result;
}

SHIM_API JSValue qjs_shim_new_function(JSContext *ctx, ManagedCallback cb,
                                       const char *name, int length)
{
    if (s_count >= MAX_CALLBACKS) {
        return JS_UNDEFINED;
    }
    int id = s_count++;
    s_callbacks[id] = cb;
    return JS_NewCFunction2(ctx, (JSCFunction *)trampoline, name, length,
                            JS_CFUNC_generic_magic, id);
}

SHIM_API void qjs_shim_reset(void)
{
    s_count = 0;
}

/* ── Module loader trampolines ── */

typedef int (*ManagedNormalize)(void *ctx, const char *base, const char *name,
                                char *out_buf, int out_buf_len);
typedef int (*ManagedReadFile)(const char *name, char *out_buf, int out_buf_len);

static ManagedNormalize s_normalize_cb;
static ManagedReadFile s_read_file_cb;

static char *normalize_trampoline(JSContext *ctx, const char *base,
                                   const char *name, void *opaque)
{
    char buf[512];
    int len = s_normalize_cb(ctx, base, name, buf, sizeof(buf));
    if (len <= 0) return NULL;
    char *result = js_malloc(ctx, len + 1);
    if (!result) return NULL;
    memcpy(result, buf, len);
    result[len] = '\0';
    return result;
}

static JSModuleDef *loader_trampoline(JSContext *ctx, const char *name, void *opaque)
{
    /* Query length */
    int len = s_read_file_cb(name, NULL, 0);
    if (len <= 0) return NULL;
    /* Allocate and read — +1 for null terminator required by JS_Eval */
    char *buf = js_malloc(ctx, len + 1);
    if (!buf) return NULL;
    s_read_file_cb(name, buf, len);
    buf[len] = '\0';
    JSValue val = JS_Eval(ctx, buf, len, name,
                          JS_EVAL_TYPE_MODULE | JS_EVAL_FLAG_COMPILE_ONLY);
    js_free(ctx, buf);
    if (JS_IsException(val)) return NULL;
    JSModuleDef *m = (JSModuleDef *)JS_VALUE_GET_PTR(val);
    JS_FreeValue(ctx, val);
    return m;
}

SHIM_API void qjs_shim_set_module_loader(JSContext *ctx,
                                          ManagedNormalize normalize_cb,
                                          ManagedReadFile read_file_cb)
{
    s_normalize_cb = normalize_cb;
    s_read_file_cb = read_file_cb;
    JS_SetModuleLoaderFunc(JS_GetRuntime(ctx),
                           normalize_trampoline, loader_trampoline, NULL);
}

/* Workaround for Mono P/Invoke bug: JS_GetPropertyStr with a JSValue returned
   from a prior P/Invoke call mismarshals the 16-byte struct. Taking u/tag as
   separate int64s avoids the struct-by-value issue. */
SHIM_API JSValue qjs_shim_get_property_str(JSContext *ctx,
                                            int64_t obj_u, int64_t obj_tag,
                                            const char *prop)
{
    JSValue obj;
    int64_t *pu = (int64_t *)&obj;
    pu[0] = obj_u;
    pu[1] = obj_tag;
    return JS_GetPropertyStr(ctx, obj, prop);
}

SHIM_API int qjs_shim_is_array(JSContext *ctx, int64_t val_u, int64_t val_tag)
{
    JSValue val;
    int64_t *pv = (int64_t *)&val;
    pv[0] = val_u;
    pv[1] = val_tag;
    return JS_IsArray(ctx, val);
}

SHIM_API JSValue qjs_shim_new_float32array(JSContext *ctx, const float *data, int count)
{
    JSValue ab = JS_NewArrayBufferCopy(ctx, (const uint8_t *)data, (size_t)count * sizeof(float));
    if (JS_IsException(ab))
        return ab;
    JSValue result = JS_NewTypedArray(ctx, 1, &ab, JS_TYPED_ARRAY_FLOAT32);
    JS_FreeValue(ctx, ab);
    return result;
}

SHIM_API JSValue qjs_shim_new_int32array(JSContext *ctx, const int32_t *data, int count)
{
    JSValue ab = JS_NewArrayBufferCopy(ctx, (const uint8_t *)data, (size_t)count * sizeof(int32_t));
    if (JS_IsException(ab))
        return ab;
    JSValue result = JS_NewTypedArray(ctx, 1, &ab, JS_TYPED_ARRAY_INT32);
    JS_FreeValue(ctx, ab);
    return result;
}

SHIM_API JSValue qjs_shim_eval_module(JSContext *ctx,
                                       const char *source, int source_len,
                                       const char *filename)
{
    JSValue compiled = JS_Eval(ctx, source, source_len, filename,
                               JS_EVAL_TYPE_MODULE | JS_EVAL_FLAG_COMPILE_ONLY);
    if (JS_IsException(compiled))
        return compiled;

    JSModuleDef *m = (JSModuleDef *)JS_VALUE_GET_PTR(compiled);

    if (JS_ResolveModule(ctx, compiled) < 0) {
        JS_FreeValue(ctx, compiled);
        return JS_ThrowInternalError(ctx, "module resolve failed");
    }

    JSValue eval_result = JS_EvalFunction(ctx, compiled);
    if (JS_IsException(eval_result))
        return eval_result;
    JS_FreeValue(ctx, eval_result);

    return JS_GetModuleNamespace(ctx, m);
}
