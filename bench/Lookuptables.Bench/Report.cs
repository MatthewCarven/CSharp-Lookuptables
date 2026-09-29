using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lookuptables.Bench;

public static class Report
{
    public static string Environment()
    {
        string cpu = RuntimeInformation.ProcessArchitecture.ToString();
        if (OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? cpu;
        }
        return $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {cpu} " +
               $"({System.Environment.ProcessorCount} logical cores)";
    }

    public static string Describe(ScenarioResult r)
    {
        Scenario s = r.Scenario;
        string keys = s.Kind switch
        {
            KeyKind.UpperAlpha => $"{s.KeyLength}-letter A-Z words",
            KeyKind.SequentialIds => $"{s.KeyLength}-byte big-endian sequential IDs drawn from [0, {4L * s.Pool:N0})",
            _ => $"{s.KeyLength}-byte random records",
        };
        return $"{keys}; {s.Pool:N0} generated -> {r.PoolDistinct:N0} distinct pre-loaded; " +
               $"{s.Inserts:N0} inserts; {s.Searches:N0} searches (50% hits); {s.Prefixes:N0} prefix queries of {s.PrefixLength} symbols";
    }

    public static string ToMarkdown(IReadOnlyList<ScenarioResult> scenarios, string environment, int reps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# C# results");
        sb.AppendLine();
        sb.AppendLine($"- Environment: {environment}");
        sb.AppendLine($"- Median of {reps} measured repetitions after warm-up; every repetition rebuilds the structure.");
        sb.AppendLine("- Build = bulk load of the pre-loaded keys (FrozenSet: pre-loaded + inserted keys, it is immutable).");
        sb.AppendLine("- Memory = managed heap growth caused by the structure itself (the keys are shared, not counted).");
        sb.AppendLine();

        foreach (ScenarioResult r in scenarios)
        {
            sb.AppendLine($"## {r.Scenario.Title}");
            sb.AppendLine();
            sb.AppendLine($"Python source: `{r.Scenario.PythonSource}`. {Describe(r)}.");
            sb.AppendLine();
            sb.AppendLine("| Structure | Kind | Build (ms) | Insert (ns/op) | Search (ns/op) | Prefix (µs/query) | Memory (MB) | Search speed-up vs List | Search time vs HashSet |");
            sb.AppendLine("|---|---|--:|--:|--:|--:|--:|--:|--:|");

            double? listSearch = r.Results.FirstOrDefault(x => x.Structure.StartsWith("List<T>"))?.SearchNsPerOp;
            double hashSearch = r.Results.First(x => x.Structure == "HashSet<T>").SearchNsPerOp;
            foreach (StructureResult x in r.Results)
            {
                string vsList = listSearch is { } l ? $"{l / x.SearchNsPerOp:N0}x" : "-";
                sb.AppendLine(
                    $"| {x.Structure} | {KindLabel(x.Family)} | {x.BuildMs:N2} | {(x.InsertNsPerOp is { } i ? i.ToString("N1") : "n/a")} | " +
                    $"{x.SearchNsPerOp:N1} | {x.PrefixUsPerQuery:N2} | {x.MemoryMB:N1} | {vsList} | {x.SearchNsPerOp / hashSearch:N2}x |");
            }
            if (r.Skipped.Count > 0)
            {
                sb.AppendLine($"\nSkipped because an average lookup would scan more than {r.Scenario.MaxMeanScan:N0} keys " +
                              $"(effectively a linear scan at this size): {string.Join("; ", r.Skipped)}.");
            }
            foreach (StructureResult x in r.Results.Where(x => x.Shape is not null))
                sb.AppendLine($"\nShape of {x.Structure}: {x.Shape}.");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string ToJson(IReadOnlyList<ScenarioResult> scenarios, string environment) =>
        JsonSerializer.Serialize(new { environment, scenarios }, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        });

    public static string KindLabel(Family family) => family switch
    {
        Family.PythonPort => "Python port",
        Family.CSharpLookupTable => "C# lookup table",
        Family.BuiltIn => "Built-in",
        Family.Adaptive => "Adaptive (new)",
        _ => family.ToString(),
    };
}
