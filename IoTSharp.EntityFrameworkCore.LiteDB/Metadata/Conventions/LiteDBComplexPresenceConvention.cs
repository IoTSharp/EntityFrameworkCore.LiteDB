using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Metadata.Conventions;

internal sealed class LiteDBComplexPresenceConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        foreach (var complex in entity.GetDeclaredComplexProperties())
            AddPresence(entity, complex, []);
    }

    private static void AddPresence(IConventionEntityType entity, IConventionComplexProperty complex, IReadOnlyList<string> parentPath)
    {
        var path = parentPath.Append(complex.Name).ToArray();
        if (complex.IsNullable)
        {
            var name = "__complexPresence_" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(string.Join(".", path)));
            if (entity.FindProperty(name) != null)
                throw new NotSupportedException($"Entity '{entity.DisplayName()}' uses the reserved LiteDB complex presence property '{name}'.");
            entity.Builder.Property(typeof(bool), name)!.HasAnnotation(LiteDBComplexPresence.PathAnnotation, path);
            complex.Builder.HasAnnotation(LiteDBComplexPresence.PropertyAnnotation, name);
        }
        foreach (var nested in complex.ComplexType.GetComplexProperties()) AddPresence(entity, nested, path);
    }
}
