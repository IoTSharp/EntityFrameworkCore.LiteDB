using System;
using System.Globalization;
using System.Threading;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace IoTSharp.EntityFrameworkCore.LiteDB.ValueGeneration.Internal;

internal sealed class LiteDBIntegerValueGenerator<TValue> : ValueGenerator<TValue>
{
    private long _current;
    private readonly object _lock = new();

    public LiteDBIntegerValueGenerator(long initialValue = 0) => _current = initialValue;

    public void Bump(long value)
    {
        lock (_lock) _current = Math.Max(_current, value);
    }

    public override bool GeneratesTemporaryValues => false;

    public override TValue Next(EntityEntry entry)
    {
        lock (_lock)
        {
            return (TValue)Convert.ChangeType(checked(++_current), typeof(TValue), CultureInfo.InvariantCulture);
        }
    }
}
