using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace IoTSharp.EntityFrameworkCore.LiteDB.ValueGeneration.Internal;

internal sealed class LiteDBValueGeneratorSelector(
    ValueGeneratorSelectorDependencies dependencies,
    ILiteDBStoreProvider storeProvider) : ValueGeneratorSelector(dependencies)
{
    public override bool TrySelect(IProperty property, ITypeBase typeBase, out ValueGenerator valueGenerator)
    {
        if (property.GetValueGeneratorFactory() == null && property.FindAnnotation("Relational:DefaultValue")?.Value is { } literal)
        {
            valueGenerator = new LiteDBLiteralValueGenerator(property.GetValueComparer().Snapshot(literal));
            return true;
        }
        var clrType = property.ClrType.UnwrapNullableType();
        if (property.GetValueGeneratorFactory() == null && clrType.IsInteger())
        {
            valueGenerator = (ValueGenerator)typeof(ILiteDBStore)
                .GetMethod(nameof(ILiteDBStore.GetIntegerValueGenerator))!
                .MakeGenericMethod(clrType).Invoke(storeProvider.Store, [property]);
            return true;
        }

        return base.TrySelect(property, typeBase, out valueGenerator);
    }

}

internal sealed class LiteDBLiteralValueGenerator(object value) : ValueGenerator
{
    public override bool GeneratesTemporaryValues => false;
    protected override object NextValue(EntityEntry entry) => value;
}
