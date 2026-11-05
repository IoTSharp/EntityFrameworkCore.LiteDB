using System;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

/// <summary>Provider type mapping used by LiteDB runtime and generated EF Core models.</summary>
/// <remarks>This provider infrastructure API may change with EF Core internal contracts.</remarks>
public sealed class LiteDBTypeMapping : CoreTypeMapping
{
    /// <summary>Gets the immutable object mapping prototype used by generated models.</summary>
    /// <remarks>EF clones this prototype with each property's CLR type, comparer and converter parameters.</remarks>
    public static LiteDBTypeMapping Default { get; } = new(typeof(object));

    /// <summary>Creates a mapping for a CLR type and its optional EF value comparers.</summary>
    public LiteDBTypeMapping(Type clrType, ValueComparer comparer = null, ValueComparer keyComparer = null)
        : base(new CoreTypeMappingParameters(clrType, null, comparer, keyComparer, null, null, null, null))
    {
    }

    private LiteDBTypeMapping(CoreTypeMappingParameters parameters) : base(parameters) { }

    /// <inheritdoc />
    protected override CoreTypeMapping Clone(CoreTypeMappingParameters parameters)
        => new LiteDBTypeMapping(parameters);

    /// <inheritdoc />
    public override CoreTypeMapping WithComposedConverter(
        ValueConverter converter,
        ValueComparer comparer = null,
        ValueComparer keyComparer = null,
        CoreTypeMapping elementMapping = null,
        JsonValueReaderWriter jsonValueReaderWriter = null)
        => new LiteDBTypeMapping(Parameters.WithComposedConverter(converter, comparer, keyComparer, elementMapping, jsonValueReaderWriter));
}
