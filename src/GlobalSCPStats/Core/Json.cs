using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

namespace GlobalSCPStats.Core;

/// <summary>
/// Minimal JSON codec. DataContractJsonSerializer cannot be used on the SCP:SL server because Unity's
/// System.Runtime.Serialization lacks the configuration types that Mono's machine.config declares, so
/// every serializer call throws ConfigurationErrorsException.
/// </summary>
internal static class Json
{
    private const int MaxDepth = 32;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static Dictionary<string, object?> ParseObject(byte[] bytes)
    {
        string text;
        try { text = Utf8.GetString(bytes); }
        catch (ArgumentException) { throw new SerializationException("JSON is not valid UTF-8."); }
        var reader = new Reader(text);
        reader.SkipWhitespace();
        if (reader.Value(0) is not Dictionary<string, object?> root) throw new SerializationException("Expected a JSON object.");
        reader.SkipWhitespace();
        if (!reader.AtEnd) throw new SerializationException("Unexpected data after JSON value.");
        return root;
    }

    public static string? String(Dictionary<string, object?> obj, string key, bool required = false)
    {
        if (!obj.TryGetValue(key, out object? value) || value == null)
            return required ? throw Missing(key) : null;
        return value as string ?? throw Invalid(key);
    }

    public static long Long(Dictionary<string, object?> obj, string key, long fallback = 0)
    {
        if (!obj.TryGetValue(key, out object? value) || value == null) return fallback;
        if (value is Number number && long.TryParse(number.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long result))
            return result;
        throw Invalid(key);
    }

    public static int Int(Dictionary<string, object?> obj, string key, int fallback = 0)
    {
        long value = Long(obj, key, fallback);
        return value is < int.MinValue or > int.MaxValue ? throw Invalid(key) : (int)value;
    }

    public static double Double(Dictionary<string, object?> obj, string key)
    {
        if (!obj.TryGetValue(key, out object? value) || value == null) return 0;
        if (value is Number number && double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            return result;
        throw Invalid(key);
    }

    public static bool Bool(Dictionary<string, object?> obj, string key)
    {
        if (!obj.TryGetValue(key, out object? value) || value == null) return false;
        return value is bool flag ? flag : throw Invalid(key);
    }

    public static Dictionary<string, object?>? Object(Dictionary<string, object?> obj, string key)
    {
        if (!obj.TryGetValue(key, out object? value) || value == null) return null;
        return value as Dictionary<string, object?> ?? throw Invalid(key);
    }

    public static List<object?>? Array(Dictionary<string, object?> obj, string key)
    {
        if (!obj.TryGetValue(key, out object? value) || value == null) return null;
        return value as List<object?> ?? throw Invalid(key);
    }

    public static Dictionary<string, long> LongMap(Dictionary<string, object?> obj, string key)
    {
        var result = new Dictionary<string, long>();
        Dictionary<string, object?>? map = Object(obj, key);
        if (map == null) return result;
        foreach (string name in map.Keys) result[name] = Long(map, name);
        return result;
    }

    private static SerializationException Missing(string key) => new($"JSON member '{key}' is required.");
    private static SerializationException Invalid(string key) => new($"JSON member '{key}' has an invalid value.");

    internal sealed class Number(string text)
    {
        public string Text { get; } = text;
    }

    private sealed class Reader(string text)
    {
        private int position;

        public bool AtEnd => position >= text.Length;

        public void SkipWhitespace()
        {
            while (position < text.Length && text[position] is ' ' or '\t' or '\r' or '\n') position++;
        }

        public object? Value(int depth)
        {
            if (depth > MaxDepth) throw new SerializationException("JSON is nested too deeply.");
            if (AtEnd) throw new SerializationException("Unexpected end of JSON.");
            char c = text[position];
            switch (c)
            {
                case '{': return ReadObject(depth);
                case '[': return ReadArray(depth);
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                    throw new SerializationException("Unexpected character in JSON.");
            }
        }

        private Dictionary<string, object?> ReadObject(int depth)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            position++;
            SkipWhitespace();
            if (Peek() == '}') { position++; return result; }
            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"') throw new SerializationException("Expected a JSON property name.");
                string key = ReadString();
                SkipWhitespace();
                if (Peek() != ':') throw new SerializationException("Expected ':' in JSON object.");
                position++;
                SkipWhitespace();
                result[key] = Value(depth + 1);
                SkipWhitespace();
                char next = Peek();
                position++;
                if (next == '}') return result;
                if (next != ',') throw new SerializationException("Expected ',' or '}' in JSON object.");
            }
        }

        private List<object?> ReadArray(int depth)
        {
            var result = new List<object?>();
            position++;
            SkipWhitespace();
            if (Peek() == ']') { position++; return result; }
            while (true)
            {
                SkipWhitespace();
                result.Add(Value(depth + 1));
                SkipWhitespace();
                char next = Peek();
                position++;
                if (next == ']') return result;
                if (next != ',') throw new SerializationException("Expected ',' or ']' in JSON array.");
            }
        }

        private string ReadString()
        {
            position++;
            var builder = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw new SerializationException("Unterminated JSON string.");
                char c = text[position++];
                if (c == '"') return builder.ToString();
                if (c < 0x20) throw new SerializationException("Control character in JSON string.");
                if (c != '\\') { builder.Append(c); continue; }
                if (AtEnd) throw new SerializationException("Unterminated JSON string.");
                char escape = text[position++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (position + 4 > text.Length ||
                            !ushort.TryParse(text.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                            throw new SerializationException("Invalid JSON unicode escape.");
                        builder.Append((char)code);
                        position += 4;
                        break;
                    default: throw new SerializationException("Invalid JSON escape.");
                }
            }
        }

        private Number ReadNumber()
        {
            int start = position;
            if (Peek() == '-') position++;
            while (!AtEnd && (char.IsDigit(text[position]) || text[position] is '.' or 'e' or 'E' or '+' or '-')) position++;
            string value = text.Substring(start, position - start);
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                throw new SerializationException("Invalid JSON number.");
            return new Number(value);
        }

        private void Expect(string literal)
        {
            if (string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
                throw new SerializationException("Invalid JSON literal.");
            position += literal.Length;
        }

        private char Peek() => AtEnd ? throw new SerializationException("Unexpected end of JSON.") : text[position];
    }
}

/// <summary>Writes compact JSON with members in the order they are added.</summary>
internal sealed class JsonWriter
{
    private readonly StringBuilder builder = new();
    private bool needsComma;

    public JsonWriter BeginObject() { Separate(); builder.Append('{'); needsComma = false; return this; }
    public JsonWriter EndObject() { builder.Append('}'); needsComma = true; return this; }
    public JsonWriter BeginArray() { Separate(); builder.Append('['); needsComma = false; return this; }
    public JsonWriter EndArray() { builder.Append(']'); needsComma = true; return this; }

    public JsonWriter Name(string name)
    {
        Separate();
        WriteString(name);
        builder.Append(':');
        needsComma = false;
        return this;
    }

    public JsonWriter Value(string? value)
    {
        Separate();
        if (value == null) builder.Append("null");
        else WriteString(value);
        needsComma = true;
        return this;
    }

    public JsonWriter Value(long value) => Raw(value.ToString(CultureInfo.InvariantCulture));
    public JsonWriter Value(bool value) => Raw(value ? "true" : "false");

    public JsonWriter Value(double value)
        => Raw(double.IsNaN(value) || double.IsInfinity(value) ? "0" : value.ToString("R", CultureInfo.InvariantCulture));

    public JsonWriter Property(string name, string? value) => Name(name).Value(value);
    public JsonWriter Property(string name, long value) => Name(name).Value(value);
    public JsonWriter Property(string name, double value) => Name(name).Value(value);
    public JsonWriter Property(string name, bool value) => Name(name).Value(value);

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(builder.ToString());

    private JsonWriter Raw(string text)
    {
        Separate();
        builder.Append(text);
        needsComma = true;
        return this;
    }

    private void Separate()
    {
        if (needsComma) builder.Append(',');
        needsComma = false;
    }

    private void WriteString(string value)
    {
        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }
}
