namespace UnityJS.Runtime.Tests
{
  using NUnit.Framework;
  using Unity.Mathematics;
  using UnityJS.QJS;

  [TestFixture]
  public unsafe class JsStateExtensionsTests
  {
    JSRuntime m_Rt;
    JSContext m_Ctx;

    [SetUp]
    public void SetUp()
    {
      m_Rt = QJS.JS_NewRuntime();
      m_Ctx = QJS.JS_NewContext(m_Rt);
    }

    [TearDown]
    public void TearDown()
    {
      if (!m_Ctx.IsNull)
        QJS.JS_FreeContext(m_Ctx);
      if (!m_Rt.IsNull)
        QJS.JS_FreeRuntime(m_Rt);
    }

    [Test]
    public void Float3_Roundtrip()
    {
      var input = new float3(1.5f, 2.5f, 3.5f);
      var jsObj = JsStateExtensions.Float3ToJsObject(m_Ctx, input);
      var output = JsStateExtensions.JsObjectToFloat3(m_Ctx, jsObj);
      QJS.JS_FreeValue(m_Ctx, jsObj);

      Assert.AreEqual(input.x, output.x, 0.001f);
      Assert.AreEqual(input.y, output.y, 0.001f);
      Assert.AreEqual(input.z, output.z, 0.001f);
    }

    [Test]
    public void Quaternion_Roundtrip()
    {
      var input = quaternion.identity;
      var jsObj = JsStateExtensions.QuaternionToJsObject(m_Ctx, input);
      var output = JsStateExtensions.JsObjectToQuaternion(m_Ctx, jsObj);
      QJS.JS_FreeValue(m_Ctx, jsObj);

      Assert.AreEqual(input.value.x, output.value.x, 0.001f);
      Assert.AreEqual(input.value.y, output.value.y, 0.001f);
      Assert.AreEqual(input.value.z, output.value.z, 0.001f);
      Assert.AreEqual(input.value.w, output.value.w, 0.001f);
    }

    [Test]
    public void QuaternionToEuler_Identity()
    {
      var euler = JsStateExtensions.QuaternionToEuler(quaternion.identity);
      Assert.AreEqual(0f, euler.x, 0.01f);
      Assert.AreEqual(0f, euler.y, 0.01f);
      Assert.AreEqual(0f, euler.z, 0.01f);
    }

    [Test]
    public void QuaternionToEuler_90Y()
    {
      var q = quaternion.EulerYXZ(0, math.radians(90f), 0);
      var euler = JsStateExtensions.QuaternionToEuler(q);
      Assert.AreEqual(0f, euler.x, 0.5f);
      Assert.AreEqual(90f, euler.y, 0.5f);
      Assert.AreEqual(0f, euler.z, 0.5f);
    }
  }
}
