namespace IoTSharp.EntityFrameworkCore.LiteDB.Infrastructure.Internal;

internal sealed class LiteDBModelValidator(ModelValidatorDependencies dependencies) : ModelValidator(dependencies)
{
    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        foreach (var entity in model.GetEntityTypes())
        {
            if (entity.GetComplexProperties().Any())
                throw new NotSupportedException($"LiteDB complex properties on '{entity.DisplayName()}' are not yet implemented. Use an owned type or a value converter. See SUPPORT.md (LDB-006).");
            if (entity.GetProperties().Any(p => p.Name == "_id" || p.Name.StartsWith("__index", StringComparison.Ordinal) || p.Name.StartsWith("__key", StringComparison.Ordinal)))
                throw new NotSupportedException($"Entity '{entity.DisplayName()}' uses a reserved BSON field name (_id or __index*). Rename the EF property.");
            if (entity.GetProperties().Any(p => p.ValueGenerated == ValueGenerated.OnAddOrUpdate
                || p.FindAnnotation("Relational:ComputedColumnSql") != null || p.FindAnnotation("Relational:DefaultValueSql") != null))
                throw new NotSupportedException($"LiteDB does not implement SQL defaults, computed SQL columns, or database-generated rowversion on '{entity.DisplayName()}'. Use application-generated values or concurrency tokens. See SUPPORT.md (LDB-007).");
        }
        base.Validate(model, logger);
    }
}
