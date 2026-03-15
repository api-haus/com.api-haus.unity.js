namespace UnityJS.Entities.PlayModeTests
{
  using Core;
  using NUnit.Framework;
  using QJS;
  using Unity.Mathematics;

  [JsBridge("TestMarshalStruct")]
  public struct TestMarshalStruct
  {
    public int Count;
    public float Speed;
    public float3 Position;
  }

  [TestFixture]
  public unsafe class JsBridgeMarshalTests : JsBridgeTestFixture
  {
    public override void SetUp()
    {
      base.SetUp();
      JsComponentRegistry.RegisterAllBridges(Ctx);
    }

    [Test]
    public void Marshal_NonComponentStruct_FromJsObject()
    {
      EvalGlobalVoid("var obj = { Count: 42, Speed: 3.5, Position: { x: 1, y: 2, z: 3 } }");
      var jsVal = EvalGlobal("obj");
      var result = JsBridge.Marshal<TestMarshalStruct>(Ctx, jsVal);
      QJS.JS_FreeValue(Ctx, jsVal);

      Assert.AreEqual(42, result.Count);
      Assert.AreEqual(3.5f, result.Speed, 0.001f);
      Assert.AreEqual(1f, result.Position.x, 0.001f);
      Assert.AreEqual(2f, result.Position.y, 0.001f);
      Assert.AreEqual(3f, result.Position.z, 0.001f);
    }

    [Test]
    public void Marshal_NonComponentStruct_GlobalRegistered()
    {
      // The auto-registered struct should be on globalThis
      var name = EvalGlobal("TestMarshalStruct.__name");
      var namePtr = QJS.JS_ToCString(Ctx, name);
      var nameStr = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)namePtr);
      QJS.JS_FreeCString(Ctx, namePtr);
      QJS.JS_FreeValue(Ctx, name);

      Assert.AreEqual("TestMarshalStruct", nameStr);
    }

    [Test]
    public void Marshal_ReaderRegistered()
    {
      Assert.IsTrue(JsBridgeMarshal<TestMarshalStruct>.Reader != null,
        "Marshal reader should be registered after bridge init");
    }
  }
}
