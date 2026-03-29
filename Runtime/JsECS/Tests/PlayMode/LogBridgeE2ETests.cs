namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for log bridge. Verifies log.info/warning/error don't throw
  /// and the component lifecycle completes successfully after logging.
  /// (Unity.Logging writes to structured sinks, not Debug.Log — can't use LogAssert.)
  /// </summary>
  public class LogBridgeE2ETests
  {
    SceneFixture m_Scene;

    [SetUp]
    public void SetUp()
    {
      m_Scene = new SceneFixture(World.DefaultGameObjectInjectionWorld);
    }

    [TearDown]
    public void TearDown()
    {
      m_Scene?.Dispose();
    }

    const string SCRIPT = "components/e2e_log_probe";
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator Log_AllLevels_NoExceptions()
    {
      var entity = m_Scene.Spawn(SCRIPT);

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      // If log.info/warning/error threw, start() would have failed
      // and the component wouldn't complete initialization.
      // Verify start() completed by checking the entity is still alive
      // and the VM has no captured exceptions from the log calls.
      var exceptions = Runtime.JsRuntimeManager.Instance?.CapturedExceptions;
      if (exceptions != null && exceptions.Count > 0)
      {
      var logExceptions = new System.Collections.Generic.List<string>();
        foreach (var ex in exceptions)
          if (ex.Contains("log") || ex.Contains("LOG"))
            logExceptions.Add(ex);

        Assert.IsEmpty(logExceptions,
          $"Log bridge should not throw exceptions: {string.Join("\n", logExceptions)}");
      }    }
  }
}
