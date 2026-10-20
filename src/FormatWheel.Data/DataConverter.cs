using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FormatWheel.Sdk;

namespace FormatWheel.Data;

// Конвертер структурированных данных: читает файл в общее дерево (Tree) и пишет его в другом формате.
public sealed class DataConverter : IConverter
{
    private const string FormatAxis = "data.format";
    private const string LayoutAxis = "data.layout";
    private const string SortAxis = "data.sort";

    // лепесток: подпись, подсказка, расширение файла
    private sealed record Target(string Id, string Label, string Hint, string Extension);

    private static readonly Target[] Targets =
    {
        new("json", "JSON", L.ForApi, "json"),
        new("yaml", "YAML", L.ForConfigs, "yaml"),
        new("xml", "XML", L.Markup, "xml"),
        new("toml", "TOML", L.ForSettings, "toml"),
        new("csv", "CSV", L.ForExcel, "csv"),
    };

    private static readonly Dictionary<string, string> Inputs = new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "json", [".jsonc"] = "json", [".geojson"] = "json",
        [".yaml"] = "yaml", [".yml"] = "yaml",
        [".xml"] = "xml",
        [".toml"] = "toml",
        [".csv"] = "csv", [".tsv"] = "csv",
    };

    public string Name => L.Name;
    public bool IsSlow => false; // текстовые файлы конвертируются мгновенно
    public int MaxParallel => Environment.ProcessorCount;

    public bool CanHandle(string path) => Inputs.ContainsKey(Path.GetExtension(path));

    public IReadOnlyList<WheelOption> MenuFor(WheelSlot slot, IReadOnlyList<string> files) => slot switch
    {
        WheelSlot.Primary => Targets.Select(t => new WheelOption(FormatAxis, t.Id, t.Label, t.Hint)).ToList(),
        // для JSON и XML: с отступами или в одну строку
        WheelSlot.Ctrl => new[]
        {
            WheelOption.Reset(LayoutAxis, L.Pretty, L.WithIndents),
            new WheelOption(LayoutAxis, "compact", L.Compact, L.OneLine),
        },
        WheelSlot.Alt => new[]
        {
            WheelOption.Reset(SortAxis, L.AsIs, L.KeepOrder),
            new WheelOption(SortAxis, "sort", L.SortKeys, L.SortHint),
        },
        _ => Array.Empty<WheelOption>()
    };

    public string Convert(string source, Recipe recipe, Action<double> report, CancellationToken ct)
    {
        string from = Inputs[Path.GetExtension(source)];
        // по умолчанию JSON, а сам JSON — в YAML
        string to = recipe.Get<string>(FormatAxis) ?? (from == "json" ? "yaml" : "json");
        var target = Targets.First(t => t.Id == to);
        bool compact = recipe.Has(LayoutAxis);

        string text = File.ReadAllText(source);
        object? data = from switch
        {
            "json" => Formats.ReadJson(text),
            "yaml" => Formats.ReadYaml(text),
            "xml" => Formats.ReadXml(text),
            "toml" => Formats.ReadToml(text),
            _ => Formats.ReadCsv(text),
        };
        report(0.5);
        ct.ThrowIfCancellationRequested();

        if (recipe.Has(SortAxis)) data = Tree.Sorted(data);

        string output = OutputPath.New(source, target.Extension);
        string result = to switch
        {
            "json" => Formats.WriteJson(data, compact),
            "yaml" => Formats.WriteYaml(data),
            "xml" => Formats.WriteXml(data, compact),
            "toml" => Formats.WriteToml(data),
            _ => Formats.WriteCsv(data),
        };
        // CSV с BOM, иначе Excel откроет кириллицу кракозябрами
        File.WriteAllText(output, result, new UTF8Encoding(encoderShouldEmitUTF8Identifier: to == "csv"));

        report(1);
        return output;
    }
}
