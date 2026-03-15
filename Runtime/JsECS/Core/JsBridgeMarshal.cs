namespace UnityJS.Entities.Core
{
  using QJS;

  public static class JsBridgeMarshal<T> where T : unmanaged
  {
    public static unsafe delegate*<JSContext, JSValue, T> Reader;
  }

  public static class JsBridge
  {
    public static unsafe T Marshal<T>(JSContext ctx, JSValue obj) where T : unmanaged
      => JsBridgeMarshal<T>.Reader(ctx, obj);
  }
}
