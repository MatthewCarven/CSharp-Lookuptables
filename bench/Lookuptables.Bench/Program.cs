using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Lookuptables.Bench;

const string Usage = """
    Lookuptables.Bench - C# port of MatthewCarven/Python-Lookuptables, benchmarked against .NET collections.

    Usage: dotnet run -c Release --project bench/Lookuptables.Bench -- [suite...] [options]

    Suites (default: python):
      python   The in-memory Python scripts (Bench, Bench2D, Bench3D, BenchBinary/BenchSet/Bench4d)
      scale    The same structures at 1,000,000 keys (structures averaging >1,000 keys per bucket skipped)
      disk     Port of BenchDisk2.py (defaults to a ~125 MB database; use --disk-records 250000 for 16 GB)
      all      python + scale + disk
      bdn      BenchmarkDotNet microbenchmarks; remaining arguments go to BenchmarkDotNet (e.g. --job short)

    Options:
      --reps N            measured repetitions per structure (default 7)
      --seed N            random seed (default 42)
      --out DIR           where results are written (default ./results)
      --disk-dir DIR      working directory for the disk suite (default ./disk-bench)
      --disk-records N    records to generate (default 2000)
      --disk-lookups N    indexed lookups (default 131070, as in BenchDisk2.py)
      --keep-disk         keep the generated disk files
    """;

if (args.Contains("-h") || args.Contains("--help"))
{
    Console.WriteLine(Usage);
    return 0;
}

if (args.Length > 0 && args[0] == "bdn")
{
    var config = DefaultConfig.Instance.WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(40));
    BenchmarkSwitcher.FromTypes([typeof(LookupBenchmarks<,>)]).Run(args[1..], config);
    return 0;
}

var suites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
int reps = 7, seed = 42, diskRecords = 2000, diskLookups = 131_070;
string outDir = "results", diskDir = "disk-bench";
bool keepDisk = false;

for (int i = 0; i < args.Length; i++)
{
    string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
    switch (args[i])
    {
        case "--reps": reps = int.Parse(Value()); break;
        case "--seed": seed = int.Parse(Value()); break;
        case "--out": outDir = Value(); break;
        case "--disk-dir": diskDir = Value(); break;
        case "--disk-records": diskRecords = int.Parse(Value()); break;
        case "--disk-lookups": diskLookups = int.Parse(Value()); break;
        case "--keep-disk": keepDisk = true; break;
        case "python" or "scale" or "disk": suites.Add(args[i]); break;
        case "all": suites.UnionWith(["python", "scale", "disk"]); break;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'.\n\n{Usage}");
            return 1;
    }
}
if (suites.Count == 0)
    suites.Add("python");

#if DEBUG
Console.WriteLine("WARNING: Debug build - timings are meaningless. Use -c Release.\n");
#endif

string environment = Report.Environment();
Console.WriteLine(environment);
Directory.CreateDirectory(outDir);

var scenarios = new List<Scenario>();
if (suites.Contains("python"))
{
    scenarios.AddRange(
    [
        new("bench", "Bench.py - 1 layer, 10k words", "Bench.py", KeyKind.UpperAlpha, 5, 10_000, 10_000, 10_000, 1_000, 3, MaxKeysPerBucket: null),
        new("bench2d", "Bench2D.py - 2 layers, 50k words", "Bench2D.py", KeyKind.UpperAlpha, 5, 50_000, 10_000, 10_000, 1_000, 3, null),
        new("bench3d", "Bench3D.py - 3 layers, 100k words", "Bench3D.py", KeyKind.UpperAlpha, 6, 100_000, 1_000, 1_000, 1_000, 3, null),
        new("binary", "BenchBinary.py / BenchSet.py / Bench4d.py - 100k binary records", "BenchBinary.py, BenchSet.py, Bench4d.py",
            KeyKind.Bytes, 16, 100_000, 1_000, 1_000, 1_000, 3, null),
    ]);
}
if (suites.Contains("scale"))
{
    scenarios.AddRange(
    [
        new("scale-alpha", "Scale - 1M words", "(beyond the Python scripts)", KeyKind.UpperAlpha, 6, 1_000_000, 10_000, 1_000_000, 1_000, 3, MaxKeysPerBucket: 1_000),
        new("scale-binary", "Scale - 1M binary records", "(beyond the Python scripts)", KeyKind.Bytes, 16, 1_000_000, 10_000, 1_000_000, 1_000, 3, 1_000),
    ]);
}

var harness = new Harness(reps, minWarmup: TimeSpan.FromMilliseconds(500));
var results = new List<ScenarioResult>();
foreach (Scenario s in scenarios)
{
    Console.WriteLine($"\n== {s.Title}");
    results.Add(s.Kind == KeyKind.UpperAlpha
        ? harness.Run(s, DatasetFactory.Alpha(s.KeyLength, s.Pool, s.Inserts, s.Searches, s.Prefixes, s.PrefixLength, seed), StructureCatalog.Alpha())
        : harness.Run(s, DatasetFactory.Bytes(s.KeyLength, s.Pool, s.Inserts, s.Searches, s.Prefixes, s.PrefixLength, seed), StructureCatalog.Bytes()));
}

string markdown = Report.ToMarkdown(results, environment, reps);
if (suites.Contains("disk"))
{
    Console.WriteLine();
    markdown += "\n" + DiskBenchmark.Run(new DiskOptions(Path.GetFullPath(diskDir), diskRecords, 65_535, diskLookups,
        LinearSamples: 5, BatchRecords: 1024, keepDisk, seed));
}

string stem = Path.Combine(outDir, "csharp-" + string.Join("-", suites.Order()));
File.WriteAllText(stem + ".md", markdown);
if (results.Count > 0)
    File.WriteAllText(stem + ".json", Report.ToJson(results, environment));
Console.WriteLine($"\n{markdown}\nWritten to {Path.GetFullPath(stem)}.md");
return 0;
