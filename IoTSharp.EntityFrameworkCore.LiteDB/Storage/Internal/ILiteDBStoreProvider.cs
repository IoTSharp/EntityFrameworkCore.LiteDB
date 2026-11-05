namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal interface ILiteDBStoreProvider
{
    ILiteDBStore Store { get; }
    Guid SessionId { get; }
}
