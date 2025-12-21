using System;
using System.Collections.Generic;

namespace Ehm93.VS.Shared;

public static class FunctionUtils
{
    public static Func<double, double> Sigmoid(double center, double k) =>
        x => 1.0 / (1.0 + Math.Exp(-k * (x - center)));

    public static Func<double, double> MemoizeStep(double step, Func<double, double> fn)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Step must be positive.");

        var cache = new Dictionary<double, double>();

        return input =>
        {
            double rounded = Math.Round(input / step) * step;

            if (!cache.TryGetValue(rounded, out var result))
            {
                result = fn(rounded);
                cache[rounded] = result;
            }

            return result;
        };
    }

    public static Func<double, double> MemoizeStepBounded(
        double step,
        double minInclusive,
        double maxInclusive,
        Func<double, double> fn)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Step must be positive.");
        if (maxInclusive <= minInclusive) throw new ArgumentException("maxInclusive must be greater than minInclusive.");

        int size = (int)Math.Ceiling((maxInclusive - minInclusive) / step) + 1;
        var results = new double[size];
        var computed = new bool[size];

        return input =>
        {
            int index = (int)Math.Round((input - minInclusive) / step);

            if (index < 0 || index >= size)
                throw new ArgumentOutOfRangeException(nameof(input), $"Input {input} out of memoized range [{minInclusive}, {maxInclusive}].");

            if (!computed[index])
            {
                double eval = minInclusive + index * step;
                results[index] = fn(eval);
                computed[index] = true;
            }

            return results[index];
        };
    }

    public static Func<TResult> MemoizeFor<TResult>(TimeSpan duration, Func<TResult> fn)
    {
        Cache<TResult>? cache = null;

        return () =>
        {
            var now = DateTime.UtcNow;
            var current = cache;

            if (current != null && now < current.ExpiresAt)
                return current.Value;

            // Recompute value
            var newValue = fn();
            var newCache = new Cache<TResult>(newValue, now + duration);
            cache = newCache;

            return newValue;
        };
    }

    public static Func<TResult> Memoize<TResult>(Func<TResult> fn)
    {
        Cache<TResult>? cache = null;
        return () =>
        {
            var current = cache;

            if (current != null)
                return current.Value;

            var newValue = fn();
            var newCache = new Cache<TResult>(newValue, DateTime.MaxValue);
            cache = newCache;
            return newValue;
        };
    }


    // Immutable state snapshot
    private class Cache<TResult>
    {
        public readonly TResult Value;
        public readonly DateTime ExpiresAt;
        public Cache(TResult value, DateTime expiresAt)
        {
            Value = value;
            ExpiresAt = expiresAt;
        }
    }
}
