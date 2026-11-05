using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Metadata.Conventions;

internal sealed class LiteDBConventionSetBuilder(ProviderConventionSetBuilderDependencies dependencies) : ProviderConventionSetBuilder(dependencies)
{
    public override ConventionSet CreateConventionSet()
    {
        var conventions = base.CreateConventionSet();
        conventions.ModelFinalizingConventions.Add(new LiteralDefaultConvention());
        return conventions;
    }

    private sealed class LiteralDefaultConvention : IModelFinalizingConvention
    {
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var property in modelBuilder.Metadata.GetEntityTypes().SelectMany(type => type.GetProperties()))
            {
                if (property.FindAnnotation("Relational:DefaultValue")?.Value is { } defaultValue)
                {
                    property.Builder.ValueGenerated(ValueGenerated.OnAdd);
                    if (property.ValueGenerated != ValueGenerated.Never)
                        property.SetValueGeneratorFactory((p, type) => new LiteDBLiteralValueGenerator(defaultValue));
                }
            }
        }
    }
}
