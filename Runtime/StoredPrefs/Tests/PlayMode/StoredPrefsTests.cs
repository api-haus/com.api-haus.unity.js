namespace StoredPrefs.PlayModeTests
{
  using System.IO;
  using NUnit.Framework;
  using Unity.Collections;

  [TestFixture]
  public class StoredPrefsTests
  {
    [SetUp]
    public void SetUp() => PrefsStore.Initialize(64);

    [TearDown]
    public void TearDown() => PrefsStore.Dispose();

    [Test]
    public void SetNumber_GetNumber_RoundTrips()
    {
      var key = new FixedString32Bytes("test.number");
      PrefsStore.SetNumber(in key, 42.5);
      Assert.AreEqual(42.5, PrefsStore.GetNumber(in key), 0.001);
    }

    [Test]
    public void SetString_GetString_RoundTrips()
    {
      var key = new FixedString32Bytes("test.string");
      var val = new FixedString64Bytes("hello");
      PrefsStore.SetString(in key, in val);
      Assert.AreEqual("hello", PrefsStore.GetString(in key).ToString());
    }

    [Test]
    public void IsSet_ReturnsFalse_WhenKeyMissing()
    {
      var key = new FixedString32Bytes("missing.key");
      Assert.IsFalse(PrefsStore.IsSet(in key));
    }

    [Test]
    public void IsSet_ReturnsTrue_WhenNonZero()
    {
      var key = new FixedString32Bytes("flag.on");
      PrefsStore.SetNumber(in key, 1);
      Assert.IsTrue(PrefsStore.IsSet(in key));
    }

    [Test]
    public void IsSet_ReturnsFalse_WhenZero()
    {
      var key = new FixedString32Bytes("flag.off");
      PrefsStore.SetNumber(in key, 0);
      Assert.IsFalse(PrefsStore.IsSet(in key));
    }

    [Test]
    public void Overwrite_ExistingKey_UpdatesValue()
    {
      var key = new FixedString32Bytes("overwrite");
      PrefsStore.SetNumber(in key, 1);
      PrefsStore.SetNumber(in key, 99);
      Assert.AreEqual(99, PrefsStore.GetNumber(in key), 0.001);
    }

    [Test]
    public void GetNumber_ReturnsDefault_WhenKeyMissing()
    {
      var key = new FixedString32Bytes("no.such.key");
      Assert.AreEqual(7.0, PrefsStore.GetNumber(in key, 7.0), 0.001);
    }

    [Test]
    public void GetString_ReturnsDefault_WhenKeyMissing()
    {
      var key = new FixedString32Bytes("no.such.str");
      var def = new FixedString64Bytes("fallback");
      Assert.AreEqual("fallback", PrefsStore.GetString(in key, in def).ToString());
    }

    [Test]
    public void SaveLoad_RoundTrips()
    {
      var path = Path.Combine(Path.GetTempPath(), "stored_prefs_test.txt");
      try
      {
        var numKey = new FixedString32Bytes("save.number");
        var strKey = new FixedString32Bytes("save.string");
        PrefsStore.SetNumber(in numKey, 3.14);
        var strVal = new FixedString64Bytes("world");
        PrefsStore.SetString(in strKey, in strVal);

        PrefsPersistence.SaveTo(path);

        PrefsStore.Clear();
        Assert.AreEqual(0, PrefsStore.GetNumber(in numKey), 0.001);

        PrefsPersistence.LoadFrom(path);
        Assert.AreEqual(3.14, PrefsStore.GetNumber(in numKey), 0.001);
        Assert.AreEqual("world", PrefsStore.GetString(in strKey).ToString());
      }
      finally
      {
        if (File.Exists(path))
          File.Delete(path);
      }
    }
  }
}
