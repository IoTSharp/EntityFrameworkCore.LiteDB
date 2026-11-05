using System.Globalization;
using LiteDB;

namespace IoTSharp.EntityFrameworkCore.LiteDB.Storage.Internal;

internal static class LiteDBValueSerialization
{
    private static readonly BsonMapper Mapper = new()
    {
        TrimWhitespace = false,
        EmptyStringToNull = false,
        SerializeNullValues = true,
        EnumAsInteger = true
    };

    public static BsonValue Write(IProperty property, object value)
    {
        var converter = property.GetTypeMapping().Converter;
        return Serialize(converter?.ProviderClrType ?? property.ClrType, converter == null ? value : converter.ConvertToProvider(value));
    }

    public static object Read(IProperty property, BsonValue value)
    {
        var converter = property.GetTypeMapping().Converter;
        var result = Deserialize(converter?.ProviderClrType ?? property.ClrType, value);
        return converter == null ? result : converter.ConvertFromProvider(result);
    }

    private static BsonValue Serialize(Type type, object value)
    {
        if (value == null) return BsonValue.Null;
        type = type.UnwrapNullableType();
        if (type == typeof(DateTime))
        {
            var date = (DateTime)value;
            return new BsonDocument { ["ticks"] = date.Ticks, ["kind"] = (int)date.Kind };
        }
        if (type == typeof(DateTimeOffset)) return ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture);
        if (type == typeof(DateOnly)) return ((DateOnly)value).ToString("O", CultureInfo.InvariantCulture);
        if (type == typeof(TimeOnly)) return ((TimeOnly)value).Ticks;
        if (type == typeof(TimeSpan)) return ((TimeSpan)value).Ticks;
        if (type == typeof(ulong)) return (decimal)(ulong)value;
        if (type == typeof(char)) return value.ToString();
        if (type == typeof(string)) return (string)value;
        var elementType = type.TryGetElementType(typeof(IEnumerable<>));
        if (type != typeof(byte[]) && elementType != null)
            return new BsonArray(((IEnumerable)value).Cast<object>().Select(element => Serialize(elementType, element)));
        return Mapper.Serialize(type, value);
    }

    private static object Deserialize(Type type, BsonValue value)
    {
        if (value.IsNull) return null;
        type = type.UnwrapNullableType();
        if (type == typeof(DateTime) && value.IsDocument) return new DateTime(value["ticks"].AsInt64, (DateTimeKind)value["kind"].AsInt32);
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value.AsString, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(DateOnly)) return DateOnly.Parse(value.AsString, CultureInfo.InvariantCulture);
        if (type == typeof(TimeOnly)) return new TimeOnly(value.AsInt64);
        if (type == typeof(TimeSpan)) return TimeSpan.FromTicks(value.AsInt64);
        if (type == typeof(ulong)) return (ulong)value.AsDecimal;
        if (type == typeof(char)) return value.AsString[0];
        if (type == typeof(string)) return value.AsString;
        var elementType = type.TryGetElementType(typeof(IEnumerable<>));
        if (type != typeof(byte[]) && elementType != null)
        {
            if (type.IsArray)
            {
                var array = Array.CreateInstance(elementType, value.AsArray.Count);
                for (var i = 0; i < array.Length; i++) array.SetValue(Deserialize(elementType, value.AsArray[i]), i);
                return array;
            }
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
            foreach (var element in value.AsArray) list.Add(Deserialize(elementType, element));
            return list;
        }
        return Mapper.Deserialize(type, value);
    }

    public static BsonValue Key(IEnumerable<BsonValue> values)
        => new(Convert.ToHexString(BsonSerializer.Serialize(new BsonDocument { ["k"] = new BsonArray(values) })));
}
