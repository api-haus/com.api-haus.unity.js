namespace UnityJS.Entities.Tests
{
  using NUnit.Framework;
  using Runtime;

  /// <summary>
  /// Test wrapper over <see cref="JsEvalUtility"/> that fails assertions on JS exceptions.
  /// </summary>
  public static class JsEval
  {
    public static int Int(string expr)
    {
      var (value, error) = JsEvalUtility.EvalInt(expr);
      if (error != null) Assert.Fail($"JS exception: {error}");
      return value;
    }

    public static double Double(string expr)
    {
      var (value, error) = JsEvalUtility.EvalDouble(expr);
      if (error != null) Assert.Fail($"JS exception: {error}");
      return value;
    }

    public static bool Bool(string expr)
    {
      var (value, error) = JsEvalUtility.EvalBool(expr);
      if (error != null) Assert.Fail($"JS exception: {error}");
      return value;
    }

    public static void Void(string expr)
    {
      var error = JsEvalUtility.EvalVoid(expr);
      if (error != null) Assert.Fail($"JS exception: {error}");
    }
  }
}
