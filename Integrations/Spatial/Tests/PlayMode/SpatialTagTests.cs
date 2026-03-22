using NUnit.Framework;

namespace MiniSpatial.Tests
{
  public class SpatialTagTests
  {
    [Test]
    public void Hash_Deterministic()
    {
      SpatialTag a = "enemy";
      SpatialTag b = "enemy";
      Assert.AreEqual(a.hash, b.hash);
    }

    [Test]
    public void Different_Strings_Different_Hashes()
    {
      SpatialTag a = "enemy";
      SpatialTag b = "ally";
      Assert.AreNotEqual(a.hash, b.hash);
    }

    [Test]
    public void Equality()
    {
      SpatialTag a = "npc";
      SpatialTag b = "npc";
      Assert.IsTrue(a.Equals(b));
      Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Test]
    public void Implicit_Int_Conversion()
    {
      SpatialTag tag = "player";
      int hash = tag;
      Assert.AreEqual(tag.hash, hash);
    }

    [Test]
    public void Manual_Hash_Constructor()
    {
      var tag = new SpatialTag(42);
      Assert.AreEqual(42, tag.hash);
    }
  }
}
