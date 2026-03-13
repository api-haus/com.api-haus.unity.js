namespace UnityJS.Entities.PlayModeTests
{
  using System;
  using System.Diagnostics;
  using Debug = UnityEngine.Debug;

  public static class BenchmarkHarness
  {
    public struct Result
    {
      public string label;
      public double meanMs;
      public double medianMs;
      public double minMs;
      public double maxMs;
      public int iterations;
    }

    public static Result Measure(string label, int warmup, int iterations, Action action)
    {
      for (var i = 0; i < warmup; i++)
        action();

      var timings = new double[iterations];
      var sw = new Stopwatch();

      for (var i = 0; i < iterations; i++)
      {
        sw.Restart();
        action();
        sw.Stop();
        timings[i] = sw.Elapsed.TotalMilliseconds;
      }

      Array.Sort(timings);

      var sum = 0.0;
      foreach (var t in timings)
        sum += t;

      var mean = sum / iterations;
      var median = iterations % 2 == 1
        ? timings[iterations / 2]
        : (timings[iterations / 2 - 1] + timings[iterations / 2]) / 2.0;
      var min = timings[0];
      var max = timings[iterations - 1];

      Debug.Log(
        $"BENCHMARK: {label} | mean={mean:F4}ms median={median:F4}ms min={min:F4}ms max={max:F4}ms (n={iterations})"
      );

      return new Result
      {
        label = label,
        meanMs = mean,
        medianMs = median,
        minMs = min,
        maxMs = max,
        iterations = iterations,
      };
    }
  }
}
