using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace FormatWheel.Data;

// Чтение каждого формата в Tree и запись из него.
internal static class Formats
{
    // ---------- JSON ----------

    public static object? ReadJson(string text)
    {
        // JSONC тоже: комментарии и висячие запятые из конфигов VS Code и т.п.
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        return FromJson(node);
    }

    private static object? FromJson(JsonNode? node)
    {
        switch (node)
        {
            case null: return null;
            case JsonObject o:
                var obj = new Obj();
                foreach (var (key, value) in o) obj.Put(key, FromJson(value));
                return obj;
            case JsonArray a: return a.Select(FromJson).ToList();
            case JsonValue v:
                return v.GetValueKind() switch
                {
                    JsonValueKind.String => v.GetValue<string>(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => v.TryGetValue(out long l) ? l : v.TryGetValue(out decimal m) ? m : v.GetValue<double>(),
                    _ => null
                };
            default: return null;
        }
    }

    public static string WriteJson(object? data, bool compact)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = !compact,
            // кириллицу и прочее оставляем как есть, а не А
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            WriteJsonValue(w, data);
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + (compact ? "" : Environment.NewLine);
    }

    private static void WriteJsonValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); break;
            case Obj o:
                w.WriteStartObject();
                foreach (var (key, v) in o) { w.WritePropertyName(key); WriteJsonValue(w, v); }
                w.WriteEndObject();
                break;
            case List<object?> list:
                w.WriteStartArray();
                foreach (var v in list) WriteJsonValue(w, v);
                w.WriteEndArray();
                break;
            case bool b: w.WriteBooleanValue(b); break;
            case long l: w.WriteNumberValue(l); break;
            case decimal m: w.WriteNumberValue(m); break;
            // NaN/Infinity из TOML в JSON числом не записать
            case double d when double.IsFinite(d): w.WriteNumberValue(d); break;
            default: w.WriteStringValue(Tree.Text(value)); break;
        }
    }

    // ---------- YAML ----------

    public static object? ReadYaml(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        // несколько документов через --- становятся массивом
        return stream.Documents.Count switch
        {
            0 => null,
            1 => FromYaml(stream.Documents[0].RootNode),
            _ => stream.Documents.Select(d => FromYaml(d.RootNode)).ToList()
        };
    }

    private static object? FromYaml(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode m:
                var obj = new Obj();
                foreach (var (key, value) in m.Children)
                    obj.Put(key is YamlScalarNode k ? k.Value ?? "" : key.ToString(), FromYaml(value));
                return obj;
            case YamlSequenceNode s: return s.Children.Select(FromYaml).ToList();
            // в кавычках — всегда строка, без кавычек — угадываем тип
            case YamlScalarNode sc: return sc.Style == ScalarStyle.Plain ? Tree.Infer(sc.Value) : sc.Value;
            default: return null;
        }
    }

    public static string WriteYaml(object? data)
    {
        var writer = new StringWriter();
        var e = new Emitter(writer);
        e.Emit(new StreamStart());
        e.Emit(new DocumentStart());
        EmitYaml(e, data);
        e.Emit(new DocumentEnd(isImplicit: true));
        e.Emit(new StreamEnd());
        return writer.ToString();
    }

    private static void EmitYaml(IEmitter e, object? value)
    {
        switch (value)
        {
            case Obj o:
                e.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, true, MappingStyle.Block));
                foreach (var (key, v) in o) { EmitString(e, key); EmitYaml(e, v); }
                e.Emit(new MappingEnd());
                break;
            case List<object?> list:
                e.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, SequenceStyle.Block));
                foreach (var v in list) EmitYaml(e, v);
                e.Emit(new SequenceEnd());
                break;
            case null: e.Emit(new Scalar(AnchorName.Empty, TagName.Empty, "null", ScalarStyle.Plain, true, false)); break;
            case string s: EmitString(e, s); break;
            case double d when !double.IsFinite(d):
                e.Emit(new Scalar(AnchorName.Empty, TagName.Empty, double.IsNaN(d) ? ".nan" : d > 0 ? ".inf" : "-.inf", ScalarStyle.Plain, true, false));
                break;
            default: e.Emit(new Scalar(AnchorName.Empty, TagName.Empty, Tree.Text(value), ScalarStyle.Plain, true, false)); break;
        }
    }

    private static void EmitString(IEmitter e, string s)
    {
        // строку, похожую на число/true/null, берём в кавычки, иначе при чтении она станет не строкой;
        // а также 007 и 1e3: старые парсеры YAML 1.1 читают их как числа. Многострочный текст — блоком |
        bool numberLike = double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
        var style = !ReferenceEquals(Tree.Infer(s), s) || numberLike ? ScalarStyle.DoubleQuoted
            : s.Contains('\n') ? ScalarStyle.Literal
            : ScalarStyle.Any;
        e.Emit(new Scalar(AnchorName.Empty, TagName.Empty, s, style, true, true));
    }

    // ---------- XML ----------

    // <a x="1"><b>2</b><b>3</b></a> → { "a": { "@x": 1, "b": [2, 3] } }
    public static object? ReadXml(string text)
    {
        var root = XDocument.Parse(text).Root ?? throw new InvalidOperationException("empty XML");
        var obj = new Obj();
        obj.Put(root.Name.LocalName, FromXml(root));
        return obj;
    }

    private static object? FromXml(XElement e)
    {
        var attrs = e.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
        if (attrs.Count == 0 && !e.HasElements) return e.IsEmpty ? null : Tree.Infer(e.Value);

        var obj = new Obj();
        foreach (var a in attrs) obj.Put("@" + a.Name.LocalName, Tree.Infer(a.Value));
        foreach (var group in e.Elements().GroupBy(c => c.Name.LocalName))
        {
            var items = group.Select(FromXml).ToList();
            obj.Put(group.Key, items.Count == 1 ? items[0] : items);
        }
        string ownText = string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (ownText.Length > 0) obj.Put("#text", Tree.Infer(ownText));
        return obj;
    }

    public static string WriteXml(object? data, bool compact)
    {
        // объект с одним ключом — это корневой элемент, иначе оборачиваем в <root>
        XElement root = data is Obj o && o.Count == 1 && o.GetAt(0).Value is not List<object?>
            ? BuildXml(o.GetAt(0).Key, o.GetAt(0).Value)
            : BuildXml("root", data);

        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(new StringWriter(sb), new XmlWriterSettings { Indent = !compact, OmitXmlDeclaration = false }))
            new XDocument(root).Save(w);
        // StringWriter пишет UTF-16 в декларации, а файл сохраняем в UTF-8
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"") + (compact ? "" : Environment.NewLine);
    }

    private static XElement BuildXml(string name, object? value)
    {
        var el = new XElement(XmlName(name));
        switch (value)
        {
            case Obj o:
                foreach (var (key, v) in o)
                {
                    if (key.StartsWith('@') && key.Length > 1 && v is not (Obj or List<object?>)) el.SetAttributeValue(XmlName(key[1..]), Tree.Text(v));
                    else if (key == "#text") el.Add(new XText(Tree.Text(v)));
                    else if (v is List<object?> list) foreach (var item in list) el.Add(BuildXml(key, item));
                    else el.Add(BuildXml(key, v));
                }
                break;
            case List<object?> list:
                foreach (var item in list) el.Add(BuildXml("item", item));
                break;
            case null: break;
            default: el.Value = Tree.Text(value); break;
        }
        return el;
    }

    private static string XmlName(string name) => name.Length == 0 ? "_" : XmlConvert.EncodeLocalName(name);

    // ---------- TOML ----------

    public static object? ReadToml(string text) => FromToml(Toml.ToModel(text));

    private static object? FromToml(object? value)
    {
        switch (value)
        {
            case TomlTable t:
                var obj = new Obj();
                foreach (var (key, v) in t) obj.Put(key, FromToml(v));
                return obj;
            case TomlTableArray ta: return ta.Select(t => FromToml(t)).ToList();
            case TomlArray a: return a.Select(FromToml).ToList();
            case TomlDateTime dt: return dt.ToString();
            default: return value;
        }
    }

    public static string WriteToml(object? data)
    {
        // корнем TOML может быть только таблица
        var root = data is Obj o ? o : new Obj { ["items"] = data };
        return Toml.FromModel(ToTomlTable(root, inline: false));
    }

    private static TomlTable ToTomlTable(Obj obj, bool inline)
    {
        var table = new TomlTable(inline);
        foreach (var (key, v) in obj)
        {
            // в TOML нет null — такие ключи просто пропускаем
            var converted = ToToml(v, inline);
            if (converted != null) table[key] = converted;
        }
        return table;
    }

    private static object? ToToml(object? value, bool inline)
    {
        switch (value)
        {
            case null: return null;
            case Obj o: return ToTomlTable(o, inline);
            case List<object?> list when !inline && list.Count > 0 && list.All(x => x is Obj):
                // массив объектов — [[секции]]
                var ta = new TomlTableArray();
                foreach (var item in list.Cast<Obj>()) ta.Add(ToTomlTable(item, inline: false));
                return ta;
            case List<object?> list:
                var arr = new TomlArray();
                foreach (var item in list)
                {
                    var converted = ToToml(item, inline: true);
                    if (converted != null) arr.Add(converted);
                }
                return arr;
            case decimal m: return (double)m;
            default: return value;
        }
    }

    // ---------- CSV ----------

    public static object? ReadCsv(string text)
    {
        char delimiter = DetectDelimiter(text);
        var rows = ParseCsv(text, delimiter);
        if (rows.Count == 0) return new List<object?>();

        // пустые и повторные заголовки тоже должны стать уникальными ключами
        var header = new List<string>();
        foreach (var (h, i) in rows[0].Select((h, i) => (h.Trim(), i)))
        {
            string name = h.Length == 0 ? $"column{i + 1}" : h;
            while (header.Contains(name)) name += "_";
            header.Add(name);
        }

        var result = new List<object?>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count == 1 && row[0].Length == 0) continue; // пустая строка
            var obj = new Obj();
            for (int i = 0; i < header.Count; i++) obj.Put(header[i], i < row.Count ? Tree.Infer(row[i]) : null);
            result.Add(obj);
        }
        return result;
    }

    // разделитель — тот, которого больше в первой строке (русский Excel сохраняет через ;)
    private static char DetectDelimiter(string text)
    {
        string first = text.Split('\n', 2)[0];
        return new[] { ',', ';', '\t', '|' }.OrderByDescending(c => first.Count(x => x == c)).First();
    }

    private static List<List<string>> ParseCsv(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new List<string>(); }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    public static string WriteCsv(object? data)
    {
        // строки таблицы: массив, или единственный массив внутри объекта, или сам объект одной строкой
        IEnumerable<object?> items = data switch
        {
            List<object?> list => list,
            Obj o when o.Count == 1 && o.GetAt(0).Value is List<object?> inner => inner,
            _ => new[] { data }
        };

        var rows = items.Select(item =>
        {
            var flat = new Obj();
            if (item is Obj o) Flatten(o, "", flat);
            else flat.Put("value", item);
            return flat;
        }).ToList();

        var columns = new List<string>();
        foreach (var row in rows)
            foreach (var key in row.Keys)
                if (!columns.Contains(key)) columns.Add(key);

        var sb = new StringBuilder();
        sb.Append(string.Join(',', columns.Select(CsvField))).Append("\r\n");
        foreach (var row in rows)
            sb.Append(string.Join(',', columns.Select(c => CsvField(row.TryGetValue(c, out var v) ? Tree.Text(v) : "")))).Append("\r\n");
        return sb.ToString();
    }

    // вложенные объекты — колонками a.b.c, массивы — JSON-строкой в ячейке
    private static void Flatten(Obj obj, string prefix, Obj into)
    {
        foreach (var (key, v) in obj)
        {
            string name = prefix + key;
            if (v is Obj child) Flatten(child, name + ".", into);
            else if (v is List<object?>) into.Put(name, WriteJson(v, compact: true));
            else into.Put(name, v);
        }
    }

    private static string CsvField(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
