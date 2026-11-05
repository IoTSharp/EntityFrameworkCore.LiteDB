using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

/// <summary>Uses EF's scalar property metadata while persisting complex values as nested BSON.</summary>
internal static class LiteDBComplexPropertyExtensions
{
    public static void Validate(IEntityType entityType) => ValidateType(entityType);

    private static void ValidateType(ITypeBase type)
    {
        var fields = type.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var complex in type.GetComplexProperties())
        {
            if (complex.IsCollection)
                throw new NotSupportedException($"LiteDB provider complex collections on '{complex.DeclaringType.DisplayName()}.{complex.Name}' are not yet implemented. LiteDB supports BSON arrays; the EF collection shaper and change-tracking contract remain provider work. See SUPPORT.md (LDB-006).");
            var alias = complex.ComplexType.FindAnnotation("Relational:ContainerColumnName")?.Value as string;
            var name = alias ?? complex.Name;
            if (alias != null && (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_')
                || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')
                || string.Equals(name, "_id", StringComparison.OrdinalIgnoreCase) || name.StartsWith("__", StringComparison.Ordinal)))
                throw new NotSupportedException($"LiteDB complex BSON container '{name}' is reserved or is not a simple field identifier. See SUPPORT.md (LDB-006).");
            if (!fields.Add(name))
                throw new NotSupportedException($"LiteDB BSON field collision for complex container '{name}' on '{type.DisplayName()}'. Choose a distinct container name. See SUPPORT.md (LDB-006).");
            ValidateType(complex.ComplexType);
        }
    }

    public static IEnumerable<IProperty> GetLiteDBProperties(this ITypeBase type)
        => type.GetProperties().Concat(type.GetComplexProperties().SelectMany(p => p.ComplexType.GetLiteDBProperties()));

    public static IEnumerable<IProperty> GetLiteDBDeclaredProperties(this IEntityType type)
        => type.GetDeclaredProperties().Concat(type.GetDeclaredComplexProperties().SelectMany(p => p.ComplexType.GetLiteDBProperties()));

    public static IEnumerable<IProperty> GetLiteDBPropertiesInHierarchy(this IEntityType type)
        => type.GetAllBaseTypes().Concat(type.GetDerivedTypesInclusive()).SelectMany(t => t.GetLiteDBDeclaredProperties());

    public static IReadOnlyList<string> GetLiteDBPath(this IProperty property)
    {
        var path = new List<string> { property.Name };
        for (var type = property.DeclaringType as IComplexType; type != null; type = type.ComplexProperty.DeclaringType as IComplexType)
            path.Insert(0, type.FindAnnotation("Relational:ContainerColumnName")?.Value as string ?? type.ComplexProperty.Name);
        return path;
    }

    public static BsonValue ReadLiteDBValue(this BsonDocument document, IProperty property)
    {
        BsonValue value = document;
        foreach (var name in property.GetLiteDBPath())
        {
            if (!value.IsDocument) return BsonValue.Null;
            value = value.AsDocument[name];
        }
        return value;
    }

    public static void WriteLiteDBValue(this BsonDocument document, IProperty property, BsonValue value)
    {
        var path = property.GetLiteDBPath();
        for (var i = 0; i < path.Count - 1; i++)
        {
            if (!document[path[i]].IsDocument) document[path[i]] = new BsonDocument();
            document = document[path[i]].AsDocument;
        }
        document[path[^1]] = value;
    }
}
