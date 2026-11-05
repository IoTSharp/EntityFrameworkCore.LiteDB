using IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Infrastructure.Internal;

internal sealed class LiteDBModelValidator(ModelValidatorDependencies dependencies) : ModelValidator(dependencies)
{
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        foreach (var entity in model.GetEntityTypes())
        {
            LiteDBComplexPropertyExtensions.Validate(entity);
            LiteDBGeneratedValues.Validate(entity);
            if (entity.GetProperties().Any(p => p.Name.Equals("_id", StringComparison.OrdinalIgnoreCase)
                || p.Name.StartsWith("__index", StringComparison.OrdinalIgnoreCase)
                || p.Name.StartsWith("__key", StringComparison.OrdinalIgnoreCase)
                || p.Name.StartsWith("__complexPresence_", StringComparison.OrdinalIgnoreCase) && !LiteDBComplexPresence.IsPresenceProperty(p)))
                throw new NotSupportedException($"Entity '{entity.DisplayName()}' uses a reserved BSON field name (_id or __index*). Rename the EF property.");
            if (entity.GetLiteDBProperties().Any(p => (p.ValueGenerated == ValueGenerated.OnAddOrUpdate && LiteDBGeneratedValues.FindExpression(p) == null)
                || p.FindAnnotation("Relational:ComputedColumnSql") != null || p.FindAnnotation("Relational:DefaultValueSql") != null))
                throw new NotSupportedException($"LiteDB does not implement SQL defaults, computed SQL columns, or database-generated rowversion on '{entity.DisplayName()}'. Use application-generated values or concurrency tokens. See SUPPORT.md (LDB-007).");
        }
        base.Validate(model, logger);
    }
}
