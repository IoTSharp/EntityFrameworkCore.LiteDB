using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

/// <summary>Evaluates generated expressions through LiteDB, using EF's store-generated sidecar.</summary>
internal static class LiteDBGeneratedValues
{
    public static string FindExpression(IProperty property)
        => property.FindAnnotation("LiteDB:ComputedExpression")?.Value as string
            ?? property.FindAnnotation("LiteDB:DefaultExpression")?.Value as string;

    public static bool ShouldGenerate(IUpdateEntry entry, IProperty property)
        => property.ValueGenerated != ValueGenerated.Never && FindExpression(property) != null
            && (property.FindAnnotation("LiteDB:ComputedExpression") != null
                || entry.EntityState == EntityState.Added
                && property.GetValueComparer().Equals(entry.GetCurrentValue(property), property.Sentinel));

    public static void Apply(IUpdateEntry entry, BsonDocument document, IReadOnlyList<IProperty> properties)
    {
        // Evaluate all expressions against the same ordinary document. Generated-on-generated
        // dependencies are rejected by validation, so property enumeration cannot change results.
        var values = new List<(IProperty Property, object Value)>();
        foreach (var property in properties.Where(p => ShouldGenerate(entry, p)))
        {
            var nativeValue = BsonExpression.Create(FindExpression(property)).ExecuteScalar(document);
            var value = LiteDBValueSerialization.Read(property, nativeValue);
            if (!property.IsNullable && value == null)
                throw new DbUpdateException($"LiteDB generated required property '{property.DeclaringType.DisplayName()}.{property.Name}' as null.", [entry]);
            values.Add((property, value));
        }
        foreach (var (property, value) in values)
        {
            document.WriteLiteDBValue(property, LiteDBValueSerialization.Write(property, value));
            entry.SetStoreGeneratedValue(property, value);
        }
    }

    public static void Validate(IEntityType entity)
    {
        var properties = entity.GetLiteDBProperties().ToArray();
        var generated = properties.Where(p => p.ValueGenerated != ValueGenerated.Never && FindExpression(p) != null).ToArray();
        var generatedRoots = generated.Select(p => p.GetLiteDBPath()[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in generated)
        {
            if (property.IsPrimaryKey() || property.IsForeignKey())
                throw new NotSupportedException("LiteDB generated expressions cannot target a primary or foreign key. Use ordinary application-generated keys.");
            if (property.FindAnnotation("LiteDB:ComputedExpression") != null && property.FindAnnotation("LiteDB:DefaultExpression") != null)
                throw new NotSupportedException("A LiteDB property cannot have both a default and a computed expression.");
            var expression = BsonExpression.Create(FindExpression(property));
            if (!expression.IsScalar)
                throw new NotSupportedException("LiteDB generated expressions must return one scalar value.");
            if (expression.Fields.Any(field => field == "$" || field == "*" || field == "_id" || field.StartsWith("__", StringComparison.Ordinal)
                || generatedRoots.Contains(field.TrimStart('$', '.').Split('.')[0])))
                throw new NotSupportedException("LiteDB generated expressions may read only ordinary fields; generated values, keys and internal fields are unavailable.");
        }
    }

    public static void ApplyComputed(BsonDocument document, IReadOnlyList<IProperty> properties)
    {
        var values = new List<(IProperty Property, object Value)>();
        foreach (var property in properties.Where(p => p.ValueGenerated != ValueGenerated.Never
                     && p.FindAnnotation("LiteDB:ComputedExpression")?.Value is string))
        {
            var value = LiteDBValueSerialization.Read(property, BsonExpression.Create(FindExpression(property)).ExecuteScalar(document));
            if (!property.IsNullable && value == null)
                throw new DbUpdateException($"LiteDB generated required property '{property.DeclaringType.DisplayName()}.{property.Name}' as null.");
            values.Add((property, value));
        }
        foreach (var (property, value) in values)
            document.WriteLiteDBValue(property, LiteDBValueSerialization.Write(property, value));
    }
}
