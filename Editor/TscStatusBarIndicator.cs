using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityJS.Runtime;

namespace UnityJS.Editor
{
  [InitializeOnLoad]
  static class TsStatusBarIndicator
  {
    const double PollInterval = 1.0;
    const double PanelCheckInterval = 2.0;

    static VisualElement s_Container;
    static Image s_Icon;
    static double s_LastPollTime;
    static double s_LastPanelCheck;
    static int s_LastErrorCount;
    static int s_LastSuccessCount;

    static TsStatusBarIndicator()
    {
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
      var existing = visualTree.Q("ts-status-container");
      existing?.RemoveFromHierarchy();

      s_Container = new VisualElement
      {
        name = "ts-status-container",
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

      s_Icon = new Image { name = "ts-status-icon", style = { width = 16, height = 16 } };

      s_Container.Add(s_Icon);
      s_Container.AddManipulator(new Clickable(OnClick));
      visualTree.Add(s_Container);

      EditorApplication.update -= OnEditorUpdate;
      EditorApplication.update += OnEditorUpdate;

      UpdateIcon();
    }

    static void OnEditorUpdate()
    {
      var now = EditorApplication.timeSinceStartup;

      if (now - s_LastPollTime >= PollInterval)
      {
        s_LastPollTime = now;

        var errCount = JsTranspiler.ErrorCount;
        var succCount = JsTranspiler.SuccessCount;
        if (errCount != s_LastErrorCount || succCount != s_LastSuccessCount)
        {
          s_LastErrorCount = errCount;
          s_LastSuccessCount = succCount;
          UpdateIcon();
        }
      }

      if (now - s_LastPanelCheck > PanelCheckInterval)
      {
        s_LastPanelCheck = now;
        if (s_Container == null || s_Container.panel == null)
          Initialize();
      }
    }

    static void UpdateIcon()
    {
      if (s_Icon == null)
        return;

      if (!JsTranspiler.IsInitialized)
      {
        s_Icon.image = EditorGUIUtility.IconContent("console.warnicon.sml")?.image;
        s_Container.tooltip = "Sucrase transpiler not initialized";
        return;
      }

      if (JsTranspiler.ErrorCount > 0)
      {
        s_Icon.image = EditorGUIUtility.IconContent("console.erroricon.sml")?.image;
        s_Container.tooltip = $"TS transpilation: {JsTranspiler.ErrorCount} error(s)\n{JsTranspiler.LastError}\nClick to open Console";
        return;
      }

      s_Icon.image = EditorGUIUtility.IconContent("TestPassed")?.image;
      s_Container.tooltip = $"TS transpiler ready ({JsTranspiler.SuccessCount} transpiled)";
    }

    static void OnClick()
    {
      if (JsTranspiler.ErrorCount > 0)
        EditorApplication.ExecuteMenuItem("Window/General/Console");
    }
  }
}
