using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

readonly struct JsonElement : IEnumerable<JsonElement>
{
    enum JsonType { Value, Array, Object }

    readonly object[] array;

    readonly Dictionary<string, object> @object;

    readonly object value;

    readonly JsonType type;

    static readonly JavaScriptSerializer serializer = new() { MaxJsonLength = int.MaxValue, RecursionLimit = int.MaxValue };

    JsonElement(object value)
    {
        if (value is object[] array) { this.array = array; type = JsonType.Array; }
        else if (value is Dictionary<string, object> @object) { this.@object = @object; type = JsonType.Object; }
        else this.value = value;
    }

    public JsonElement this[string key] => new(@object[key]);

    public JsonElement this[int index] => new(array[index]);

    public T Value<T>() => type == JsonType.Value ? (T)value : throw new NotSupportedException();

    public bool _(string key, out JsonElement element)
    {
        if (type != JsonType.Object) throw new NotSupportedException();
        var @bool = @object.TryGetValue(key, out var value); element = new(value);
        return @bool;
    }

    public static JsonElement Parse(string value) => new(serializer.DeserializeObject(value));

    public IEnumerator<JsonElement> GetEnumerator() => (type switch
    {
        JsonType.Object => @object.Values.Select(_ => new JsonElement(_)),
        JsonType.Array => array.Select(_ => new JsonElement(_)),
        _ => throw new NotSupportedException()
    }).GetEnumerator();

    public IEnumerable<JsonElement> _(string key) => _(type switch
    {
        JsonType.Array => array,
        JsonType.Object => @object,
        _ => throw new NotSupportedException()
    }).Where(_ => _.Key == key).Select(_ => new JsonElement(_.Value));

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    static IEnumerable<KeyValuePair<string, object>> _(object value) => value switch
    {
        object[] array => array.SelectMany(_),
        Dictionary<string, object> @object => @object.Concat(@object.SelectMany(_ => JsonElement._(_.Value))),
        _ => []
    };
}