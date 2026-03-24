namespace StoredPrefs
{
  using System.Globalization;
  using System.IO;
  using Unity.Collections;
  using UnityEngine;

  public static class PrefsPersistence
  {
    static string DefaultPath =>
      Path.Combine(Application.persistentDataPath, "stored_prefs.txt");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
      PrefsStore.Initialize(128);
      LoadFrom(DefaultPath);
      Application.quitting += OnQuit;
    }

    static void OnQuit()
    {
      SaveTo(DefaultPath);
      PrefsStore.Dispose();
    }

    public static void LoadFrom(string path)
    {
      if (!File.Exists(path))
        return;

      var lines = File.ReadAllLines(path);
      foreach (var line in lines)
      {
        if (string.IsNullOrWhiteSpace(line))
          continue;

        var eq = line.IndexOf('=');
        if (eq < 0)
          continue;

        var name = line.Substring(0, eq).Trim();
        var raw = line.Substring(eq + 1).Trim();

        var key = new FixedString32Bytes(name);

        if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
        {
          var str = raw.Substring(1, raw.Length - 2);
          var fstr = new FixedString64Bytes(str);
          PrefsStore.SetString(in key, in fstr);
        }
        else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
        {
          PrefsStore.SetNumber(in key, num);
        }
      }
    }

    public static void SaveTo(string path)
    {
      if (!PrefsStore.IsCreated)
        return;

      using var sw = new StreamWriter(path);
      var enumerator = PrefsStore.GetEnumerator();
      while (enumerator.MoveNext())
      {
        var pv = enumerator.Current.Value;
        var keyStr = pv.key.ToString();
        if (pv.kind == PrefValue.Kind.Number)
          sw.WriteLine($"{keyStr}={pv.numberVal.ToString(CultureInfo.InvariantCulture)}");
        else
          sw.WriteLine($"{keyStr}=\"{pv.stringVal}\"");
      }

      enumerator.Dispose();
    }

    public static void Save() => SaveTo(DefaultPath);
  }
}
