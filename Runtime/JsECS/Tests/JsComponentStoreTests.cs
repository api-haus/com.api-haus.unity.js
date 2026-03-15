namespace UnityJS.Entities.Tests
{
  using Core;
  using NUnit.Framework;

  [TestFixture]
  public unsafe class JsComponentStoreTests : JsBridgeTestFixture
  {
    public override void SetUp()
    {
      base.SetUp();
      // Register component store functions on ecs namespace
      JsComponentStore.Register(Ctx);
    }

    public override void TearDown()
    {
      JsComponentStore.Shutdown();
      base.TearDown();
    }

    [Test]
    public void Define_CreatesComponent()
    {
      EvalGlobalVoid("ecs.define('health')");
      Assert.IsTrue(JsComponentStore.IsDefined("health"));
    }

    [Test]
    public void Has_ReturnsFalseBeforeAdd()
    {
      EvalGlobalVoid("ecs.define('health')");
      var has = EvalGlobalBool("ecs.has(1, 'health')");
      Assert.IsFalse(has);
    }

    [Test]
    public void Get_ReturnsUndefinedBeforeAdd()
    {
      EvalGlobalVoid("ecs.define('health')");
      var result = EvalGlobal("ecs.get(1, 'health')");
      Assert.IsTrue(QJS.QJS.IsUndefined(result));
      QJS.QJS.JS_FreeValue(Ctx, result);
    }

    [Test]
    public void Define_DuplicateName_LogsError()
    {
      EvalGlobalVoid("ecs.define('armor')");
      // Second define with same name should log error but not throw
      EvalGlobalVoid("ecs.define('armor')");
      // Still defined
      Assert.IsTrue(JsComponentStore.IsDefined("armor"));
    }

    [Test]
    public void Define_MultipleComponents_UniqueSlots()
    {
      EvalGlobalVoid("ecs.define('comp_a')");
      EvalGlobalVoid("ecs.define('comp_b')");
      EvalGlobalVoid("ecs.define('comp_c')");
      Assert.IsTrue(JsComponentStore.IsDefined("comp_a"));
      Assert.IsTrue(JsComponentStore.IsDefined("comp_b"));
      Assert.IsTrue(JsComponentStore.IsDefined("comp_c"));
      Assert.AreEqual("comp_a", JsComponentStore.GetSlotName(0));
      Assert.AreEqual("comp_b", JsComponentStore.GetSlotName(1));
      Assert.AreEqual("comp_c", JsComponentStore.GetSlotName(2));
    }
  }
}
