using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal sealed class LiteDBTableSnapshot
{
    public LiteDBTableSnapshot(IEntityType entityType, IReadOnlyList<object[]> rows)
    {
        EntityType = entityType;
        Rows = rows;
    }

    public IEntityType EntityType { get; }
    public IReadOnlyList<object[]> Rows { get; }
}
