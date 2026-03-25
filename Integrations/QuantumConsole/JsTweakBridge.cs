namespace UnityJS.Integration.QuantumConsole
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using AOT;
  using QFSW.QC;
  using StoredPrefs;
  using Unity.Collections;
  using UnityEngine;
  using UnityJS.QJS;
  using UnityJS.Runtime;
  using static UnityJS.Runtime.QJSHelpers;

  public static class JsTweakBridge
  {
    struct TweakConstraint
    {
      public enum Kind { NumberEnum, StringEnum, Range }

      public Kind kind;
      public double[] numberValues;
      public string[] stringValues;
      public double min, max, step;
      public string description;
    }

    static readonly Dictionary<string, TweakConstraint> s_tweaks = new();
    static readonly Dictionary<string, CommandData> s_registeredCommands = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
      foreach (var cmd in s_registeredCommands.Values)
        QuantumConsoleProcessor.TryRemoveCommand(cmd);
      s_registeredCommands.Clear();
      s_tweaks.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void AutoRegister() =>
      UnityJS.Entities.Core.JsFunctionRegistry.Register("__global", RegisterGlobals);

    static unsafe void RegisterGlobals(JSContext ctx, JSValue ns)
    {
      var global = QJS.JS_GetGlobalObject(ctx);
      AddFunction(ctx, global, "param", Param_Callback, 3);
      QJS.JS_FreeValue(ctx, global);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Param_Callback(
      JSContext ctx, long thisU, long thisTag, int argc, JSValue* argv,
      long* outU, long* outTag)
    {
      if (argc < 2)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var name = QJS.ToManagedString(ctx, argv[0]);
      if (string.IsNullOrEmpty(name))
      {
        SetUndefined(outU, outTag);
        return;
      }

      var constraintVal = argv[1];
      TweakConstraint constraint;

      if (QJS.IsObject(constraintVal))
      {
        bool isArray;
        fixed (byte* pLen = LengthProp)
        {
          var lenCheck = QJS.JS_GetPropertyStr(ctx, constraintVal, pLen);
          isArray = QJS.IsNumber(lenCheck);
          QJS.JS_FreeValue(ctx, lenCheck);
        }

        constraint = isArray
          ? ParseArrayConstraint(ctx, constraintVal)
          : ParseRangeConstraint(ctx, constraintVal);
      }
      else
      {
        SetUndefined(outU, outTag);
        return;
      }

      if (argc >= 3)
        constraint.description = QJS.ToManagedString(ctx, argv[2]);

      bool alreadyRegistered = s_tweaks.ContainsKey(name);
      s_tweaks[name] = constraint;
      SetDefaultIfMissing(name, constraint);

      // Only register QC command once — handlers read s_tweaks[name] at invocation
      // time, so constraint updates take effect without re-registration.
      if (!alreadyRegistered)
        RegisterQcCommand(name, constraint);

      SetUndefined(outU, outTag);
    }

    static unsafe TweakConstraint ParseArrayConstraint(JSContext ctx, JSValue arr)
    {
      double lenD;
      fixed (byte* pLen = LengthProp)
      {
        var lenVal = QJS.JS_GetPropertyStr(ctx, arr, pLen);
        QJS.JS_ToFloat64(ctx, &lenD, lenVal);
        QJS.JS_FreeValue(ctx, lenVal);
      }

      int len = (int)lenD;

      var first = QJS.JS_GetPropertyUint32(ctx, arr, 0);
      bool isNumber = QJS.IsNumber(first);
      QJS.JS_FreeValue(ctx, first);

      if (isNumber)
      {
        var values = new double[len];
        for (int i = 0; i < len; i++)
        {
          var elem = QJS.JS_GetPropertyUint32(ctx, arr, (uint)i);
          double d;
          QJS.JS_ToFloat64(ctx, &d, elem);
          QJS.JS_FreeValue(ctx, elem);
          values[i] = d;
        }

        return new TweakConstraint { kind = TweakConstraint.Kind.NumberEnum, numberValues = values };
      }
      else
      {
        var values = new string[len];
        for (int i = 0; i < len; i++)
        {
          var elem = QJS.JS_GetPropertyUint32(ctx, arr, (uint)i);
          values[i] = QJS.ToManagedString(ctx, elem) ?? "";
          QJS.JS_FreeValue(ctx, elem);
        }

        return new TweakConstraint { kind = TweakConstraint.Kind.StringEnum, stringValues = values };
      }
    }

    static readonly byte[] LengthProp = System.Text.Encoding.UTF8.GetBytes("length\0");
    static readonly byte[] MinProp = System.Text.Encoding.UTF8.GetBytes("min\0");
    static readonly byte[] MaxProp = System.Text.Encoding.UTF8.GetBytes("max\0");
    static readonly byte[] StepProp = System.Text.Encoding.UTF8.GetBytes("step\0");

    static unsafe TweakConstraint ParseRangeConstraint(JSContext ctx, JSValue obj)
    {
      double min, max, step = 0;

      fixed (byte* pMin = MinProp, pMax = MaxProp, pStep = StepProp)
      {
        var minVal = QJS.JS_GetPropertyStr(ctx, obj, pMin);
        QJS.JS_ToFloat64(ctx, &min, minVal);
        QJS.JS_FreeValue(ctx, minVal);

        var maxVal = QJS.JS_GetPropertyStr(ctx, obj, pMax);
        QJS.JS_ToFloat64(ctx, &max, maxVal);
        QJS.JS_FreeValue(ctx, maxVal);

        var stepVal = QJS.JS_GetPropertyStr(ctx, obj, pStep);
        if (!QJS.IsUndefined(stepVal) && !QJS.IsNull(stepVal))
          QJS.JS_ToFloat64(ctx, &step, stepVal);
        QJS.JS_FreeValue(ctx, stepVal);
      }

      return new TweakConstraint
      {
        kind = TweakConstraint.Kind.Range,
        min = min,
        max = max,
        step = step
      };
    }

    static void SetDefaultIfMissing(string name, TweakConstraint c)
    {
      var key = new FixedString32Bytes(name);
      if (PrefsStore.TryGet(in key, out _))
        return;

      switch (c.kind)
      {
        case TweakConstraint.Kind.NumberEnum:
          PrefsStore.SetNumber(in key, c.numberValues[0]);
          break;
        case TweakConstraint.Kind.StringEnum:
          var sv = new FixedString64Bytes(c.stringValues[0]);
          PrefsStore.SetString(in key, in sv);
          break;
        case TweakConstraint.Kind.Range:
          PrefsStore.SetNumber(in key, c.min);
          break;
      }
    }

    static void RegisterQcCommand(string name, TweakConstraint c)
    {
      var desc = c.description ?? "";
      CommandData cmd;
      switch (c.kind)
      {
        case TweakConstraint.Kind.NumberEnum:
        case TweakConstraint.Kind.Range:
        {
          Func<double, string> handler = val => HandleNumberTweak(name, val);
          cmd = new LambdaCommandData(handler, name, desc);
          break;
        }
        case TweakConstraint.Kind.StringEnum:
        {
          Func<string, string> handler = arg => HandleStringTweak(name, arg);
          cmd = new LambdaCommandData(handler, name, desc);
          break;
        }
        default: return;
      }

      if (QuantumConsoleProcessor.TryAddCommand(cmd))
        s_registeredCommands[name] = cmd;
    }

    static string HandleNumberTweak(string name, double val)
    {
      if (!s_tweaks.TryGetValue(name, out var c))
        return $"Unknown param: {name}";

      var key = new FixedString32Bytes(name);

      if (c.kind == TweakConstraint.Kind.NumberEnum)
      {
        bool valid = false;
        foreach (var v in c.numberValues)
        {
          if (Math.Abs(v - val) < 0.0001)
          {
            valid = true;
            break;
          }
        }

        if (!valid)
          return $"Value must be one of: {string.Join(", ", c.numberValues)}";
      }
      else if (c.kind == TweakConstraint.Kind.Range)
      {
        if (val < c.min || val > c.max)
          return $"Value must be between {c.min} and {c.max}";
      }

      PrefsStore.SetNumber(in key, val);
      PrefsPersistence.Save();
      return $"{name} = {val.ToString(CultureInfo.InvariantCulture)}";
    }

    static string HandleStringTweak(string name, string arg)
    {
      if (!s_tweaks.TryGetValue(name, out var c))
        return $"Unknown param: {name}";

      var key = new FixedString32Bytes(name);

      if (string.IsNullOrEmpty(arg))
        return $"{name} = {PrefsStore.GetString(in key)}";

      bool valid = false;
      foreach (var v in c.stringValues)
      {
        if (v == arg)
        {
          valid = true;
          break;
        }
      }

      if (!valid)
        return $"Value must be one of: {string.Join(", ", c.stringValues)}";

      var fstr = new FixedString64Bytes(arg);
      PrefsStore.SetString(in key, in fstr);
      PrefsPersistence.Save();
      return $"{name} = {arg}";
    }
  }
}
