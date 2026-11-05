using System;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed class LiteDBTypeMapping : CoreTypeMapping
{
    public LiteDBTypeMapping(Type clrType, ValueComparer comparer = null, ValueComparer keyComparer = null)
        : base(new CoreTypeMappingParameters(clrType, null, comparer, keyComparer, null, null, null, null))
    {
    }

    private LiteDBTypeMapping(CoreTypeMappingParameters parameters) : base(parameters) { }

    protected override CoreTypeMapping Clone(CoreTypeMappingParameters parameters)
        => new LiteDBTypeMapping(parameters);

    public override CoreTypeMapping WithComposedConverter(
        ValueConverter converter,
        ValueComparer comparer = null,
        ValueComparer keyComparer = null,
        CoreTypeMapping elementMapping = null,
        JsonValueReaderWriter jsonValueReaderWriter = null)
        => new LiteDBTypeMapping(Parameters.WithComposedConverter(converter, comparer, keyComparer, elementMapping, jsonValueReaderWriter));
}
