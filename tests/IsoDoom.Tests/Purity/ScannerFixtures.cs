using System;
using System.Collections.Generic;
using System.Diagnostics;

// Deliberately impure code, used only to prove that DeterminismScanner
// catches each forbidden construct. Never call these.
namespace IsoDoom.Tests.Purity.Fixtures;

public class Clean
{
    private readonly Dictionary<int, int> _lookup = new();
    private readonly List<int> _items = new();

    public int Sum(int key)
    {
        int sum = _lookup.TryGetValue(key, out int v) ? v : 0;
        foreach (int item in _items)
            sum += item >> 16;
        Func<int, int> twice = x => x * 2;
        return twice(sum);
    }
}

public class FloatField { public float X; }

public class FloatProperty { public int Get(List<float> values) => values.Count; }

public class DoubleLocal
{
    public static int Half(int a)
    {
        double d = a;
        return (int)(d / 2);
    }
}

public class FloatLiteral
{
    public static bool Above(int a) => a * 1.5f > 3;
}

public class FloatInLambda
{
    public static Func<int, int> Make() => x => (int)(x * 0.5);
}

public class FloatInIterator
{
    public static IEnumerable<int> Seq(int n)
    {
        for (int i = 0; i < n; i++)
            yield return (int)Math.Sqrt(i);
    }
}

public class RandomUse
{
    public static int Roll() => new Random(1).Next();
}

public class DateTimeUse
{
    public static long Now() => DateTime.Now.Ticks;
}

public class TickCountUse
{
    public static int Now() => Environment.TickCount;
}

public class StopwatchUse
{
    public static long Now() => Stopwatch.GetTimestamp();
}

public class DictionaryIteration
{
    public static int Sum(Dictionary<int, int> d)
    {
        int sum = 0;
        foreach (KeyValuePair<int, int> kv in d)
            sum += kv.Value;
        return sum;
    }
}

public class DictionaryKeysIteration
{
    public static int Sum(Dictionary<int, int> d)
    {
        int sum = 0;
        foreach (int k in d.Keys)
            sum += k;
        return sum;
    }
}

public class HashSetIteration
{
    public static int Sum(HashSet<int> s)
    {
        int sum = 0;
        foreach (int v in s)
            sum += v;
        return sum;
    }
}
