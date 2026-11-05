using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal static class LiteDBComplexPresence
{
    public const string PathAnnotation = "LiteDB:ComplexPresencePath";
    public const string PropertyAnnotation = "LiteDB:ComplexPresenceProperty";

    public static bool IsPresenceProperty(IProperty property) => property.FindAnnotation(PathAnnotation) != null;

    public static bool IsPresent(IProperty property, object entity)
    {
        var current = entity;
        var type = (ITypeBase)property.DeclaringType.ContainingEntityType;
        foreach (var name in (string[])property.FindAnnotation(PathAnnotation)!.Value!)
        {
            if (current == null) return false;
            var complex = type.FindComplexProperty(name)!;
            current = complex.GetMemberInfo(forMaterialization: false, forSet: false) switch
            {
                PropertyInfo propertyInfo => propertyInfo.GetValue(current),
                FieldInfo fieldInfo => fieldInfo.GetValue(current),
                _ => throw new InvalidOperationException($"Complex property '{complex.Name}' has no CLR member.")
            };
            type = complex.ComplexType;
        }
        return current != null;
    }

    public static void Apply(IUpdateEntry entry, BsonDocument document, IReadOnlyList<IProperty> properties)
    {
        foreach (var property in properties.Where(IsPresenceProperty))
        {
            var present = IsPresent(property, entry.ToEntityEntry().Entity);
            document[property.Name] = present;
            entry.ToEntityEntry().Property(property.Name).CurrentValue = present;
        }
    }
}
