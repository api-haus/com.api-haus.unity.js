using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityJS.Editor
{
  [InitializeOnLoad]
  static class TscStatusBarIndicator
  {
    const double SpinInterval = 0.08;
    const double PanelCheckInterval = 2.0;

    static readonly string[] k_SpinIcons = new string[12];

    static VisualElement s_Container;
    static Image s_Icon;
    static int s_SpinFrame;
    static double s_LastSpinTime;
    static double s_LastPanelCheck;

    static TscStatusBarIndicator()
    {
      for (int i = 0; i < 12; i++)
        k_SpinIcons[i] = $"d_WaitSpin{i:D2}";

      EditorApplication.delayCall += Initialize;
    }

    static void Initialize()
    {
      var visualTree = FindStatusBarVisualTree();
      if (visualTree == null)
      {
        EditorApplication.delayCall += () =>
        {
          var vt = FindStatusBarVisualTree();
          if (vt != null)
            Inject(vt);
        };
        return;
      }

      Inject(visualTree);
    }

    static VisualElement FindStatusBarVisualTree()
    {
      var asm = typeof(UnityEditor.Editor).Assembly;
      var statusBarType = asm.GetType("UnityEditor.AppStatusBar");
      if (statusBarType == null)
        return null;

      var instances = Resources.FindObjectsOfTypeAll(statusBarType);
      if (instances.Length == 0)
        return null;

      var guiViewType = asm.GetType("UnityEditor.GUIView");
      if (guiViewType == null)
        return null;

      var prop = guiViewType.GetProperty(
        "visualTree",
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
      );
      prop ??= guiViewType.GetProperty(
        "rootVisualElement",
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
      );

      return prop?.GetValue(instances[0]) as VisualElement;
    }

    static void Inject(VisualElement visualTree)
    {
      var existing = visualTree.Q("tsc-status-container");
      existing?.RemoveFromHierarchy();

      s_Container = new VisualElement
      {
        name = "tsc-status-container",
        style =
        {
          position = Position.Absolute,
          right = 90,
          top = 0,
          bottom = 0,
          flexDirection = FlexDirection.Row,
          alignItems = Align.Center,
          justifyContent = Justify.Center,
          paddingLeft = 2,
          paddingRight = 2,
        },
      };

      s_Icon = new Image { name = "tsc-status-icon", style = { width = 16, height = 16 } };

      s_Container.Add(s_Icon);
      s_Container.AddManipulator(new Clickable(OnClick));
      visualTree.Add(s_Container);

      var compiler = TscCompiler.Instance;
      if (compiler != null)
      {
        compiler.StateChanged -= OnStateChanged;
        compiler.StateChanged += OnStateChanged;
      }
      EditorApplication.update -= OnEditorUpdate;
      EditorApplication.update += OnEditorUpdate;

      UpdateIcon();
    }

    static void OnStateChanged()
    {
      s_SpinFrame = 0;
      UpdateIcon();
    }

    static void OnEditorUpdate()
    {
      var compiler = TscCompiler.Instance;
      if (compiler != null && compiler.State == TscState.Compiling)
      {
        var now = EditorApplication.timeSinceStartup;
        if (now - s_LastSpinTime >= SpinInterval)
        {
          s_LastSpinTime = now;
          s_SpinFrame = (s_SpinFrame + 1) % 12;
          UpdateIcon();
        }
      }

      var t = EditorApplication.timeSinceStartup;
      if (t - s_LastPanelCheck > PanelCheckInterval)
      {
        s_LastPanelCheck = t;
        if (s_Container == null || s_Container.panel == null)
          Initialize();
      }
    }

    static void UpdateIcon()
    {
      if (s_Icon == null)
        return;
      var compiler = TscCompiler.Instance;
      var state = compiler?.State ?? TscState.Dead;
      s_Icon.image = IconForState(state);
      s_Container.tooltip = TooltipForState(state);
    }

    static Texture IconForState(TscState state)
    {
      var name = state switch
      {
        TscState.Dead => "d_winbtn_mac_close",
        TscState.Compiling => k_SpinIcons[s_SpinFrame],
        TscState.Success => "TestPassed",
        TscState.Error => "console.erroricon.sml",
        _ => null,
      };
      if (name == null)
        return null;
      var content = EditorGUIUtility.IconContent(name);
      return content?.image;
    }

    static string TooltipForState(TscState state)
    {
      return state switch
      {
        TscState.Dead => "tsc is not available\nClick to recompile",
        TscState.Compiling => "tsc is compiling\u2026",
        TscState.Success => "tsc compilation succeeded",
        TscState.Error => BuildErrorTooltip(),
        _ => "",
      };
    }

    static string BuildErrorTooltip()
    {
      var compiler = TscCompiler.Instance;
      var errors = compiler?.LastErrors;
      if (errors == null || errors.Count == 0)
        return "tsc compilation failed";

      var sb = new StringBuilder();
      sb.AppendLine($"tsc: {errors.Count} error(s) — click to open Console");
      var count = Math.Min(errors.Count, 5);
      for (int i = 0; i < count; i++)
        sb.AppendLine(errors[i]);
      if (errors.Count > 5)
        sb.AppendLine($"\u2026 and {errors.Count - 5} more");
      return sb.ToString().TrimEnd();
    }

    static void OnClick()
    {
      var compiler = TscCompiler.Instance;
      if (compiler == null)
        return;

      switch (compiler.State)
      {
        case TscState.Error:
          EditorApplication.ExecuteMenuItem("Window/General/Console");
          break;
        case TscState.Dead:
          EditorApplication.ExecuteMenuItem("Tools/JS/Recompile TypeScript");
          break;
      }
    }
  }
}
