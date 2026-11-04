namespace Docket.Cli;

/// <summary>Tiny argument reader: positional words plus --name value / -n value options and --flags.</summary>
public sealed class Args
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "--json", "--all", "--help", "-h", "--yes", "-y", "--watch", "--once" };
    private static readonly Dictionary<string, string> Short = new(StringComparer.Ordinal)
    {
        ["-p"] = "--project", ["-d"] = "--due", ["-P"] = "--priority", ["-l"] = "--label", ["-t"] = "--title", ["-n"] = "--limit",
    };

    public List<string> Words { get; } = [];
    private readonly Dictionary<string, List<string>> _opts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public Args(IEnumerable<string> argv)
    {
        var list = argv.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a == "--") { Words.AddRange(list.Skip(i + 1)); break; }
            var key = Short.GetValueOrDefault(a, a);
            if (Flags.Contains(key)) { _flags.Add(key); continue; }
            if (key.StartsWith("--", StringComparison.Ordinal) && key.Length > 2)
            {
                var eq = key.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0) { Add(key[..eq], key[(eq + 1)..]); continue; }
                if (i + 1 >= list.Count) throw new UsageException($"{a} needs a value.");
                Add(key, list[++i]);
                continue;
            }
            Words.Add(a);
        }
    }

    private void Add(string k, string v) { if (!_opts.TryGetValue(k, out var l)) _opts[k] = l = []; l.Add(v); }

    public bool Has(string flag) => _flags.Contains(flag);
    public string? Opt(string name) => _opts.TryGetValue(name, out var l) ? l[^1] : null;
    public IReadOnlyList<string> All(string name) => _opts.TryGetValue(name, out var l) ? l : [];

    public int? IntOpt(string name) => Opt(name) is { } s
        ? int.TryParse(s, out var n) ? n : throw new UsageException($"{name} must be a number.")
        : null;

    public string Rest(int from) => string.Join(' ', Words.Skip(from));

    public long Id(int index, string what = "task id")
    {
        if (Words.Count <= index) throw new UsageException($"Missing {what}.");
        var s = Words[index].TrimStart('#');
        return long.TryParse(s, out var id) ? id : throw new UsageException($"\"{Words[index]}\" isn't a {what}.");
    }
}

public sealed class UsageException(string message) : Exception(message);
