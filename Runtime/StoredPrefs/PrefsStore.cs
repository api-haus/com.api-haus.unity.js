namespace StoredPrefs
{
  using Unity.Burst;
  using Unity.Collections;
  using Unity.Collections.LowLevel.Unsafe;

  public struct PrefValue
  {
    public enum Kind : byte { Number, String }

    public Kind kind;
    public FixedString32Bytes key;
    public double numberVal;
    public FixedString64Bytes stringVal;

    public static PrefValue FromNumber(in FixedString32Bytes key, double v) =>
      new() { kind = Kind.Number, key = key, numberVal = v };

    public static PrefValue FromString(in FixedString32Bytes key, in FixedString64Bytes v) =>
      new() { kind = Kind.String, key = key, stringVal = v };
  }

  struct PrefsStoreKey { }

  public static class PrefsStore
  {
    static readonly SharedStatic<NativeParallelHashMap<int, PrefValue>> s_Map =
      SharedStatic<NativeParallelHashMap<int, PrefValue>>.GetOrCreate<PrefsStoreKey>();

    public static bool IsCreated => s_Map.Data.IsCreated;

    public static void Initialize(int capacity = 128)
    {
      if (s_Map.Data.IsCreated)
        s_Map.Data.Dispose();
      s_Map.Data = new NativeParallelHashMap<int, PrefValue>(capacity, Allocator.Persistent);
    }

    public static void Dispose()
    {
      if (s_Map.Data.IsCreated)
        s_Map.Data.Dispose();
    }

    public static void Clear()
    {
      if (s_Map.Data.IsCreated)
        s_Map.Data.Clear();
    }

    public static void SetNumber(in FixedString32Bytes key, double value)
    {
      var hash = key.GetHashCode();
      var pv = PrefValue.FromNumber(in key, value);
      if (s_Map.Data.ContainsKey(hash))
        s_Map.Data[hash] = pv;
      else
        s_Map.Data.Add(hash, pv);
    }

    public static void SetString(in FixedString32Bytes key, in FixedString64Bytes value)
    {
      var hash = key.GetHashCode();
      var pv = PrefValue.FromString(in key, in value);
      if (s_Map.Data.ContainsKey(hash))
        s_Map.Data[hash] = pv;
      else
        s_Map.Data.Add(hash, pv);
    }

    public static double GetNumber(in FixedString32Bytes key, double defaultValue = 0)
    {
      var hash = key.GetHashCode();
      return s_Map.Data.TryGetValue(hash, out var pv) ? pv.numberVal : defaultValue;
    }

    public static FixedString64Bytes GetString(in FixedString32Bytes key,
      in FixedString64Bytes defaultValue = default)
    {
      var hash = key.GetHashCode();
      return s_Map.Data.TryGetValue(hash, out var pv) ? pv.stringVal : defaultValue;
    }

    public static bool IsSet(in FixedString32Bytes key)
    {
      var hash = key.GetHashCode();
      return s_Map.Data.TryGetValue(hash, out var pv) && pv.numberVal != 0;
    }

    public static bool TryGet(in FixedString32Bytes key, out PrefValue value)
    {
      var hash = key.GetHashCode();
      return s_Map.Data.TryGetValue(hash, out value);
    }

    public static NativeParallelHashMap<int, PrefValue>.Enumerator GetEnumerator() =>
      s_Map.Data.GetEnumerator();
  }
}
