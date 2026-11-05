using System;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed class LiteDBTypeMappingSource : TypeMappingSource
{
    public LiteDBTypeMappingSource(TypeMappingSourceDependencies dependencies) : base(dependencies) { }

    protected override CoreTypeMapping FindMapping(in TypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        Debug.Assert(clrType != null);
        if (clrType.IsValueType || clrType == typeof(string) || clrType == typeof(byte[]))
        {
            return new LiteDBTypeMapping(clrType, clrType == typeof(byte[]) ? new ArrayStructuralComparer<byte>() : null);
        }

        var elementType = clrType.TryGetElementType(typeof(IEnumerable<>));
        if (elementType != null && (clrType.IsArray || clrType.IsAssignableFrom(typeof(List<>).MakeGenericType(elementType)))
            && (elementType.UnwrapNullableType().IsValueType || elementType == typeof(string)))
        {
            var comparer = (ValueComparer)Activator.CreateInstance(typeof(LiteDBCollectionComparer<>).MakeGenericType(clrType));
            return new LiteDBTypeMapping(clrType, comparer, comparer);
        }

        return base.FindMapping(mappingInfo);
    }
}
