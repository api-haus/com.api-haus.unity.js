#include "quickjs.h"

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
