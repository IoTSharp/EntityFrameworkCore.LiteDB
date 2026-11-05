using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed partial class LiteDBTable
{
    public int ExecuteBulk(IReadOnlyList<ValueBuffer> rows, IProperty[] properties,
        Func<ValueBuffer, object[]> values, CancellationToken cancellationToken)
    {
        BsonValue Id(ValueBuffer row) => LiteDBValueSerialization.Key(_primaryKey.Properties.Select(
            p => LiteDBValueSerialization.Write(p, row[p.GetIndex()])));
        var ids = rows.Select(Id).Distinct().ToArray();
        if (properties.Length == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ids.Length == 0 ? 0 : _documents.DeleteMany(global::LiteDB.Query.In("_id", new BsonArray(ids)));
        }
        var presentAncestors = FindBulkPresenceProperties(properties);
        var affected = 0;
        var seen = new HashSet<BsonValue>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Id(row);
            if (!seen.Add(id)) continue;
            var document = _documents.FindById(id);
            if (document == null) continue;
            // Evaluate every setter before changing any value so x=A,y=x use the old x.
            var updatedValues = values(row);
            for (var i = 0; i < properties.Length; i++)
            {
                if (!properties[i].IsNullable && updatedValues[i] == null)
                    throw new DbUpdateException($"Required property '{properties[i].Name}' is null.");
                document.WriteLiteDBValue(properties[i], LiteDBValueSerialization.Write(properties[i], updatedValues[i]));
            }
            foreach (var presence in presentAncestors) document[presence.Name] = true;
            LiteDBGeneratedValues.ApplyComputed(document, _properties);
            WriteIndexes(document);
            // LiteDB checks the expression's syntactic Document type, so a parameter holding
            // a document is insufficient. Keep field names escaped and every value parameterized.
            var parameters = new BsonDocument();
            var fields = new List<string>();
            foreach (var field in document.Where(field => field.Key != "_id"))
            {
                var parameter = "v" + parameters.Count;
                parameters[parameter] = field.Value;
                fields.Add(System.Text.Json.JsonSerializer.Serialize(field.Key) + ": @" + parameter);
            }
            var transform = BsonExpression.Create("{" + string.Join(",", fields) + "}", parameters);
            affected += _documents.UpdateMany(transform, global::LiteDB.Query.EQ("_id", id));
        }
        return affected;
    }

    private IProperty[] FindBulkPresenceProperties(IReadOnlyList<IProperty> setters)
    {
        // Presence annotations use CLR complex names; BSON paths may instead use a ToJson alias.
        var setterPaths = setters.Select(property =>
        {
            var path = new List<string>();
            for (var type = property.DeclaringType as IComplexType; type != null; type = type.ComplexProperty.DeclaringType as IComplexType)
                path.Insert(0, type.ComplexProperty.Name);
            return path;
        }).ToArray();
        return _properties.Where(property => property.FindAnnotation(LiteDBComplexPresence.PathAnnotation)?.Value is string[] path
            && setterPaths.Any(setterPath => path.Length <= setterPath.Count
                && path.SequenceEqual(setterPath.Take(path.Length), StringComparer.Ordinal))).ToArray();
    }
}
