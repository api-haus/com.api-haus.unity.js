using System.IO;
using UnityEditor;

namespace UnityJS.Integrations.Editor
{
  [InitializeOnLoad]
  static class IntegrationDetector
  {
    static readonly (string guid, string define, string rspRelPath)[] s_integrations =
    {
      ("63afb046c8423dd448ae7aba042ea63d", "UNITY_JS_PHYSICS", "Integrations/Physics/csc.rsp"),
      (
        "75469ad4d38634e559750d17036d5f7c",
        "UNITY_JS_INPUT_SYSTEM",
        "Integrations/InputSystem/csc.rsp"
      ),
      ("de4e6084e6d474788bb8c799d6b461ec", "UNITY_JS_ALINE", "Integrations/ALINE/csc.rsp"),
      (
        "fba13774ff4078041871f6741c062151",
        "UNITY_JS_CHARACTER_CONTROLLER",
        "Integrations/CharacterController/csc.rsp"
      ),
      (
        "fb24642277b1db2449da7ac148ce939d",
        "UNITY_JS_QUANTUM_CONSOLE",
        "Integrations/QuantumConsole/csc.rsp"
      ),
    };

    static IntegrationDetector()
    {
      var pkgInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
        typeof(IntegrationDetector).Assembly
      );
      if (pkgInfo == null)
        return;
      var packageRoot = pkgInfo.resolvedPath;

      var changed = false;
      foreach (var (guid, define, rspRelPath) in s_integrations)
      {
        var rspPath = Path.Combine(packageRoot, rspRelPath);
        if (!File.Exists(rspPath))
          continue;

        var depPresent = !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid));
        var content = File.ReadAllText(rspPath).Trim();
        var activeLine = $"-define:{define}";
        var commentedLine = $"#{activeLine}";

        var desired = depPresent ? activeLine : commentedLine;
        if (content != desired)
        {
          File.WriteAllText(rspPath, desired + "\n");
          changed = true;
        }
      }

      if (changed)
        AssetDatabase.Refresh();
    }
  }
}
