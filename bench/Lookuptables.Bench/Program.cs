using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Lookuptables.Bench;

const string Usage = """
    Lookuptables.Bench - C# port of MatthewCarven/Python-Lookuptables, benchmarked against .NET collections.

    Usage: dotnet run -c Release --project bench/Lookuptables.Bench -- [suite...] [options]

    Suites (default: python):
      python   The in-memory Python scripts (Bench, Bench2D, Bench3D, BenchBinary/BenchSet/Bench4d)
      scale    The same structures at 1,000,000 keys (tables whose lookups would scan >1,000 keys skipped)
      adaptive The burst trie (dynamically sized buckets) against everything, on uniform and skewed data
      burst-tune  The burst trie's split threshold, swept from 8 to 1024
      disk     Port of BenchDisk2.py (defaults to a ~125 MB database; --disk-gb 16 matches the Python script)
      all      python + scale + adaptive + disk
      bdn      BenchmarkDotNet microbenchmarks; remaining arguments go to BenchmarkDotNet (e.g. --job short)

    Options:
      --reps N            measured repetitions per structure (default 7)
      --only ID           run just the scenario with this id (e.g. adaptive-ids-1m)
      --seed N            random seed (default 42)
      --out DIR           where results are written (default ./results)
      --disk-dir DIR      working directory for the disk suite (default ./disk-bench)
      --disk-records N    records to generate (default 2000)
      --disk-gb N         size the flat file in GB instead (the index takes the same again)
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
string? only = null;
const int DiskRecordSize = 65_535; // RECORD_SIZE in BenchDisk2.py
bool keepDisk = false;

for (int i = 0; i < args.Length; i++)
{
    string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
    switch (args[i])
    {
        case "--reps": reps = int.Parse(Value()); break;
        case "--seed": seed = int.Parse(Value()); break;
        case "--only": only = Value(); break;
        case "--out": outDir = Value(); break;
        case "--disk-dir": diskDir = Value(); break;
        case "--disk-records": diskRecords = int.Parse(Value()); break;
        case "--disk-gb": diskRecords = checked((int)(double.Parse(Value()) * (1L << 30) / DiskRecordSize)); break;
        case "--disk-lookups": diskLookups = int.Parse(Value()); break;
        case "--keep-disk": keepDisk = true; break;
        case "python" or "scale" or "adaptive" or "burst-tune" or "disk": suites.Add(args[i]); break;
        case "all": suites.UnionWith(["python", "scale", "adaptive", "disk"]); break;
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

var scenarios = new List<(Scenario Scenario, bool Tune)>();
if (suites.Contains("python"))
{
    scenarios.AddRange(new Scenario[]
    {
        new("bench", "Bench.py - 1 layer, 10k words", "Bench.py", KeyKind.UpperAlpha, 5, 10_000, 10_000, 10_000, 1_000, 3, MaxMeanScan: null),
        new("bench2d", "Bench2D.py - 2 layers, 50k words", "Bench2D.py", KeyKind.UpperAlpha, 5, 50_000, 10_000, 10_000, 1_000, 3, null),
        new("bench3d", "Bench3D.py - 3 layers, 100k words", "Bench3D.py", KeyKind.UpperAlpha, 6, 100_000, 1_000, 1_000, 1_000, 3, null),
        new("binary", "BenchBinary.py / BenchSet.py / Bench4d.py - 100k binary records", "BenchBinary.py, BenchSet.py, Bench4d.py",
            KeyKind.Bytes, 16, 100_000, 1_000, 1_000, 1_000, 3, null),
    }.Select(s => (s, false)));
}
if (suites.Contains("scale"))
{
    scenarios.AddRange(new Scenario[]
    {
        new("scale-alpha", "Scale - 1M words", "(beyond the Python scripts)", KeyKind.UpperAlpha, 6, 1_000_000, 10_000, 1_000_000, 1_000, 3, MaxMeanScan: 1_000),
        new("scale-binary", "Scale - 1M binary records", "(beyond the Python scripts)", KeyKind.Bytes, 16, 1_000_000, 10_000, 1_000_000, 1_000, 3, 1_000),
    }.Select(s => (s, false)));
}

// Sequential IDs are 8-byte big-endian numbers; a 7-byte prefix query asks for one block of 256 consecutive IDs.
Scenario[] adaptiveScenarios =
[
    new("adaptive-bytes-1k", "Adaptive - 1k random binary records", "(new)", KeyKind.Bytes, 16, 1_000, 1_000, 10_000, 1_000, 3, MaxMeanScan: 1_000),
    new("adaptive-bytes-1m", "Adaptive - 1M random binary records", "(new)", KeyKind.Bytes, 16, 1_000_000, 10_000, 1_000_000, 1_000, 3, 1_000),
    new("adaptive-words-1m", "Adaptive - 1M words", "(new)", KeyKind.UpperAlpha, 6, 1_000_000, 10_000, 1_000_000, 1_000, 3, 1_000),
    new("adaptive-ids-10k", "Adaptive - 10k sequential IDs", "(new)", KeyKind.SequentialIds, 8, 10_000, 1_000, 10_000, 1_000, 7, null),
    new("adaptive-ids-1m", "Adaptive - 1M sequential IDs", "(new)", KeyKind.SequentialIds, 8, 1_000_000, 10_000, 1_000_000, 1_000, 7, 1_000),
];
if (suites.Contains("adaptive"))
    scenarios.AddRange(adaptiveScenarios.Select(s => (s, false)));
if (suites.Contains("burst-tune"))
{
    scenarios.AddRange(adaptiveScenarios.Where(s => s.Pool == 1_000_000)
        .Select(s => (s with { Id = s.Id.Replace("adaptive", "tune"), Title = s.Title.Replace("Adaptive", "Burst threshold") }, true)));
}

if (only is not null)
    scenarios.RemoveAll(x => x.Scenario.Id != only);

var harness = new Harness(reps, minWarmup: TimeSpan.FromSeconds(1.5));
var results = new List<ScenarioResult>();
foreach ((Scenario s, bool tune) in scenarios)
{
    Console.WriteLine($"\n== {s.Title}");
    results.Add(s.Kind switch
    {
        KeyKind.UpperAlpha => harness.Run(s, DatasetFactory.Alpha(s.KeyLength, s.Pool, s.Inserts, s.Searches, s.Prefixes, s.PrefixLength, seed),
            tune ? StructureCatalog.BurstThresholds<string, Lookuptables.UpperAlphaKey>() : StructureCatalog.Alpha()),
        KeyKind.Bytes => harness.Run(s, DatasetFactory.Bytes(s.KeyLength, s.Pool, s.Inserts, s.Searches, s.Prefixes, s.PrefixLength, seed),
            tune ? StructureCatalog.BurstThresholds<byte[], Lookuptables.ByteKey>() : StructureCatalog.Bytes()),
        _ => harness.Run(s, DatasetFactory.SequentialIds(s.Pool, s.Inserts, s.Searches, s.Prefixes, s.PrefixLength, seed),
            tune ? StructureCatalog.BurstThresholds<byte[], Lookuptables.ByteKey>() : StructureCatalog.Bytes()),
    });
}

string markdown = results.Count > 0
    ? Report.ToMarkdown(results, environment, reps)
    : $"# C# results\n\n- Environment: {environment}\n";
if (suites.Contains("disk"))
{
    Console.WriteLine();
    markdown += "\n" + DiskBenchmark.Run(new DiskOptions(Path.GetFullPath(diskDir), diskRecords, DiskRecordSize, diskLookups,
        LinearSamples: 5, BatchRecords: 1024, keepDisk, seed));
}

string stem = Path.Combine(outDir, "csharp-" + string.Join("-", suites.Order()));
File.WriteAllText(stem + ".md", markdown);
if (results.Count > 0)
    File.WriteAllText(stem + ".json", Report.ToJson(results, environment));
Console.WriteLine($"\n{markdown}\nWritten to {Path.GetFullPath(stem)}.md");
return 0;
