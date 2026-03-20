#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityJS.Editor
{
  /// <summary>
  /// Injects a tsc --watch state icon into the Unity Editor status bar (AppStatusBar).
  /// AppStatusBar inherits from GUIView (not EditorWindow), so we use reflection
  /// to access its visual tree and overlay a VisualElement with absolute positioning.
  /// </summary>
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
        // Retry once — status bar may not be ready yet on first domain reload frame
        EditorApplication.delayCall += () =>
        {
          var vt = FindStatusBarVisualTree();
          if (vt != null) Inject(vt);
        };
        return;
      }

      Inject(visualTree);
    }

    static VisualElement FindStatusBarVisualTree()
    {
      var asm = typeof(UnityEditor.Editor).Assembly;
      var statusBarType = asm.GetType("UnityEditor.AppStatusBar");
      if (statusBarType == null) return null;

      var instances = Resources.FindObjectsOfTypeAll(statusBarType);
      if (instances.Length == 0) return null;

      // GUIView.visualTree is the root VisualElement (internal property)
      var guiViewType = asm.GetType("UnityEditor.GUIView");
      if (guiViewType == null) return null;

      var prop = guiViewType.GetProperty("visualTree",
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
      // Fallback name used in some Unity versions
      prop ??= guiViewType.GetProperty("rootVisualElement",
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

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

      s_Icon = new Image
      {
        name = "tsc-status-icon",
        style =
        {
          width = 16,
          height = 16,
        },
      };

      s_Container.Add(s_Icon);
      s_Container.AddManipulator(new Clickable(OnClick));
      visualTree.Add(s_Container);

      TscWatchService.StateChanged -= OnStateChanged;
      TscWatchService.StateChanged += OnStateChanged;
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
      if (TscWatchService.State == TscWatchState.Compiling)
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
      if (s_Icon == null) return;
      var state = TscWatchService.State;
      s_Icon.image = IconForState(state);
      s_Container.tooltip = TooltipForState(state);
    }

    static Texture IconForState(TscWatchState state)
    {
      var name = state switch
      {
        TscWatchState.Dead => "d_winbtn_mac_close",
        TscWatchState.Idle => "d_ViewToolOrbit",
        TscWatchState.Compiling => k_SpinIcons[s_SpinFrame],
        TscWatchState.Success => "TestPassed",
        TscWatchState.Error => "console.erroricon.sml",
        _ => null,
      };
      if (name == null) return null;
      var content = EditorGUIUtility.IconContent(name);
      return content?.image;
    }

    static string TooltipForState(TscWatchState state)
    {
      return state switch
      {
        TscWatchState.Dead => "tsc --watch is not running\nClick to restart",
        TscWatchState.Idle => "tsc --watch is idle",
        TscWatchState.Compiling => "tsc is compiling…",
        TscWatchState.Success => "tsc compilation succeeded",
        TscWatchState.Error => BuildErrorTooltip(),
        _ => "",
      };
    }

    static string BuildErrorTooltip()
    {
      var errors = TscWatchService.LastErrors;
      if (errors.Count == 0)
        return "tsc compilation failed";

      var sb = new StringBuilder();
      sb.AppendLine($"tsc: {errors.Count} error(s) — click to open Console");
      var count = Math.Min(errors.Count, 5);
      for (int i = 0; i < count; i++)
        sb.AppendLine(errors[i]);
      if (errors.Count > 5)
        sb.AppendLine($"… and {errors.Count - 5} more");
      return sb.ToString().TrimEnd();
    }

    static void OnClick()
    {
      switch (TscWatchService.State)
      {
        case TscWatchState.Error:
          EditorApplication.ExecuteMenuItem("Window/General/Console");
          break;
        case TscWatchState.Dead:
          EditorApplication.ExecuteMenuItem("Tools/JS/Restart tsc --watch");
          break;
      }
    }
  }
}
#endif
