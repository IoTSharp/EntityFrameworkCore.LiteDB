using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed class LiteDBCollectionComparer<T> : ValueComparer<T>
{
    public LiteDBCollectionComparer() : base(
        (left, right) => EqualValues(left, right),
        value => HashValues(value),
        value => CopyValues(value)) { }

    private static bool EqualValues(T left, T right)
        => ReferenceEquals(left, right) || left != null && right != null
            && ((IEnumerable)left).Cast<object>().SequenceEqual(((IEnumerable)right).Cast<object>());

    private static int HashValues(T value)
    {
        if (value == null) return 0;
        var hash = new HashCode();
        foreach (var element in (IEnumerable)value) hash.Add(element);
        return hash.ToHashCode();
    }

    private static T CopyValues(T value)
    {
        if (value == null) return value;
        if (value is Array array) return (T)(object)array.Clone();
        var elementType = typeof(T).TryGetElementType(typeof(IEnumerable<>));
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
        foreach (var element in (IEnumerable)value) list.Add(element);
        return (T)list;
    }
}
