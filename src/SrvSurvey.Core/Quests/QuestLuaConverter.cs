using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lua;

namespace SrvSurvey.Core.Quests;

internal static class QuestLuaConverter
{
    public static LuaValue ToLua(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => LuaValue.Nil,
            JsonValueKind.True => new LuaValue(true),
            JsonValueKind.False => new LuaValue(false),
            JsonValueKind.Number when value.TryGetInt64(out var integer) =>
                new LuaValue(integer),
            JsonValueKind.Number => new LuaValue(value.GetDouble()),
            JsonValueKind.String => ToLuaString(value.GetString() ?? string.Empty),
            JsonValueKind.Array => ToLuaArray(value),
            JsonValueKind.Object => ToLuaObject(value),
            _ => throw new InvalidDataException(
                $"Unsupported JSON value kind '{value.ValueKind}'."),
        };
    }

    public static JsonElement ToJson(LuaValue value)
    {
        var node = ToJsonNode(value);
        using var document = JsonDocument.Parse(node?.ToJsonString() ?? "null");
        return document.RootElement.Clone();
    }

    private static LuaValue ToLuaString(string value)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp))
        {
            return ToLuaTimestamp(timestamp);
        }

        return new LuaValue(value);
    }

    private static LuaTable ToLuaArray(JsonElement value)
    {
        var table = new LuaTable(value.GetArrayLength(), 0);
        var index = 1;
        foreach (var item in value.EnumerateArray())
        {
            table[index++] = ToLua(item);
        }

        return table;
    }

    private static LuaTable ToLuaObject(JsonElement value)
    {
        var table = new LuaTable(0, value.GetPropertyCount());
        foreach (var property in value.EnumerateObject())
        {
            table[property.Name] = ToLua(property.Value);
        }

        return table;
    }

    private static LuaTable ToLuaTimestamp(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        return new LuaTable(0, 9)
        {
            ["year"] = value.Year,
            ["month"] = value.Month,
            ["day"] = value.Day,
            ["hour"] = value.Hour,
            ["min"] = value.Minute,
            ["sec"] = value.Second,
            ["wday"] = (int)value.DayOfWeek + 1,
            ["yday"] = value.DayOfYear,
            ["isdst"] = local.DateTime.IsDaylightSavingTime(),
        };
    }

    private static JsonNode? ToJsonNode(LuaValue value)
    {
        return value.Type switch
        {
            LuaValueType.Nil => null,
            LuaValueType.Boolean => JsonValue.Create(value.Read<bool>()),
            LuaValueType.Number => JsonValue.Create(value.Read<double>()),
            LuaValueType.String => JsonValue.Create(value.Read<string>()),
            LuaValueType.Table => ToJsonTable(value.Read<LuaTable>()),
            _ => throw new InvalidDataException(
                $"Lua value type '{value.TypeToString}' cannot be persisted."),
        };
    }

    private static JsonNode ToJsonTable(LuaTable table)
    {
        if (table.HashMapCount > 0 && table.ArrayLength > 0)
        {
            throw new InvalidDataException(
                "Lua tables must be either an array or a string-keyed map to be persisted.");
        }

        if (table.HashMapCount > 0)
        {
            var result = new JsonObject();
            foreach (var pair in table)
            {
                if (pair.Key.Type != LuaValueType.String)
                {
                    throw new InvalidDataException(
                        "Persisted Lua table map keys must be strings.");
                }

                result[pair.Key.Read<string>()] = ToJsonNode(pair.Value);
            }

            return result;
        }

        var array = new JsonArray();
        for (var index = 1; index <= table.ArrayLength; index++)
        {
            array.Add(ToJsonNode(table[index]));
        }

        return array;
    }
}
