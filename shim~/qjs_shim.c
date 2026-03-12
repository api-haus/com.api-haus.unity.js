#include "quickjs.h"
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
    /* Allocate and read */
    char *buf = js_malloc(ctx, len);
    if (!buf) return NULL;
    s_read_file_cb(name, buf, len);
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
