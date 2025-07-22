// Program.cs  –  .NET 8   (console-multiplexer with per-child ShowWindow flag)
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/*───────────────────────────────────────── 1 - Data types ─────────────────────────────────────────*/

record ChildSpec(string Name, string Cmd, string args, bool ShowWindow = false);

enum MsgKind { User, Llm }

record LogEntry(long Index, DateTime Timestamp, string Source, string Message, MsgKind Kind);

internal sealed class Child
{
    public required string Name;
    public required Process Proc;
    public  Task? StdOutPump;
    public  Task? StdErrPump;
}

/*───────────────────────────────────────── 2 - Program ─────────────────────────────────────────────*/

class Program
{
    // global state
    private static readonly ConcurrentQueue<LogEntry> _log = new();
    private static long _nextIndex;
    private static readonly ConcurrentDictionary<string, long> _cursor = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Child> _children = new(StringComparer.OrdinalIgnoreCase);

    static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: multiproxy <config.json>");
            return 1;
        }
        if (!System.IO.File.Exists(args[0]))
        {
            Console.Error.WriteLine($"Config file not found: {args[0]}");
            return 2;
        }
        if (!await LoadAndSpawnAsync(args[0])) return 3;
        if (_children.IsEmpty) { Console.Error.WriteLine("No children started."); return 4; }

        /* ── stdin router ─────────────────────────────────── */
        var inputTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await Console.In.ReadLineAsync()) is not null)
            {
                if (TryParseTarget(line, out var tgt, out var msg))
                {
                    if (_children.TryGetValue(tgt, out var child) && !child.Proc.HasExited)
                    {
                        await SendWithCatchUpAsync(msg, child);
                        Append("user", msg, MsgKind.User);
                    }
                    else
                        Console.Error.WriteLine($"[proxy] child \"{tgt}\" not found or exited");
                }
                else
                {
                    foreach (var c in _children.Values)
                        if (!c.Proc.HasExited)
                            await SendWithCatchUpAsync(line, c);
                    Append("user", line, MsgKind.User);
                }
               
            }
        });

        /* ── shutdown ─────────────────────────────────────── */
        await Task.WhenAll(_children.Values.Select(c => c.Proc.WaitForExitAsync()));
        foreach (var c in _children.Values) c.Proc.StandardInput.Close();
      
        await Task.WhenAll(_children.Values.SelectMany(c => new[] { c.StdOutPump??Task.CompletedTask, c.StdErrPump ?? Task.CompletedTask }));
        await inputTask;

        Console.WriteLine($"[proxy] Captured {_log.Count} messages.");
        int exitCode = _children.Values.Select(c => c.Proc.ExitCode).Where(x => x != 0).LastOrDefault();
        Console.WriteLine($"All children finished. Proxy exiting with code {exitCode}.");
        return exitCode;
    }

    /*───────────────────────────────────────── 3 - Spawn & config ─────────────────────────────────────*/

    private static async Task<bool> LoadAndSpawnAsync(string path)
    {
        try
        {
            string json = await System.IO.File.ReadAllTextAsync(path);
            var specs = JsonSerializer.Deserialize<ChildSpec[]>(json,
                          new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            var dupe = specs.GroupBy(s => s.Name).FirstOrDefault(g => g.Count() > 1);
            if (dupe != null) { Console.Error.WriteLine($"Duplicate child name \"{dupe.Key}\""); return false; }
            foreach (var s in specs) Spawn(s);
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Config error: {ex.Message}"); return false; }
    }

    private static void Spawn(ChildSpec spec)
    {
        var exe = spec.Cmd;
        var  argLine = spec.args;

        var psi = new ProcessStartInfo(exe, argLine)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = false //!spec.ShowWindow
        };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            psi.WindowStyle = spec.ShowWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!p.Start()) { Console.Error.WriteLine($"[{spec.Name}] FAILED to start {spec.Cmd}"); return; }

        var child = new Child { Name = spec.Name, Proc = p };
        child.StdOutPump = Pump(child, p.StandardOutput, Console.Out);
        child.StdErrPump = Pump(child, p.StandardError, Console.Error, ConsoleColor.Red);

        _children.TryAdd(spec.Name, child);
        Console.WriteLine($"Spawned [{spec.Name}] {spec.Cmd}  (window {(spec.ShowWindow ? "shown" : "hidden")})");
    }

    /*───────────────────────────────────────── 4 - Pumps & logging ────────────────────────────────────*/

    private static async Task Pump(Child child,
                                   System.IO.StreamReader reader,
                                   System.IO.TextWriter writer,
                                   ConsoleColor? color = null)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            long idx = Append(child.Name, line, MsgKind.Llm);
            _cursor[child.Name] = idx;

            if (color is ConsoleColor c)
            {
                var old = Console.ForegroundColor;
                Console.ForegroundColor = c;
                await writer.WriteLineAsync($"[{child.Name}] {line}");
                Console.ForegroundColor = old;
            }
            else
            {
                await writer.WriteLineAsync($"[{child.Name}] {line}");
            }
        }
    }

    private static long Append(string src, string msg, MsgKind kind)
    {
        long idx = Interlocked.Increment(ref _nextIndex) - 1;
        _log.Enqueue(new(idx, DateTime.UtcNow, src, msg, kind)); return idx;
    }

    /*───────────────────────────────────────── 5 - Catch-up sender (escaped \n) ───────────────────────*/

    private static async Task SendWithCatchUpAsync(string userText, Child target)
    {
        long last = _cursor.TryGetValue(target.Name, out var i) ? i : -1;
        var missed = _log.Where(e => e.Index > last && e.Source != target.Name)
                         .OrderBy(e => e.Index)
                         .Select(e => $"{e.Source}: {Escape(e.Message)}");
        if (missed.Count() == 0)
        {
            string payload = $"user: {Escape(userText)}";
            await target.Proc.StandardInput.WriteLineAsync(payload);
        }
        else
        {
            string payload = $"{string.Join("\\n", missed)}\\nuser: {Escape(userText)}";
            await target.Proc.StandardInput.WriteLineAsync(payload);
        }
        
        _cursor[target.Name] = _nextIndex - 1;            // mark as caught up
    }
    private static string Escape(string s) => s.Replace("\r", "").Replace("\n", "\\n");

    /*───────────────────────────────────────── 6 - Parsing helpers ───────────────────────────────────*/

    private static bool TryParseTarget(string line, out string name, out string payload)
    {
        var m = Regex.Match(line, @"^\s*/([A-Za-z0-9_\-]+):(.*)$");
        if (m.Success) { name = m.Groups[1].Value; payload = m.Groups[2].Value; return true; }
        name = payload = ""; return false;
    }

    private static (string exe, string args) SplitCommandLine(string cmd)
    {
        var m = Regex.Match(cmd.Trim(),
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? @"^(""[^""]+""|\S+)\s*(.*)$"
                : @"^('(?:\\'|[^'])*'|""(?:\\""|[^""])*""|\S+)\s*(.*)$");
        if (!m.Success) throw new ArgumentException($"Bad command: {cmd}");
        return (Trim(m.Groups[1].Value), m.Groups[2].Value);
    }
    private static string Trim(string s) =>
        (s.StartsWith('"') && s.EndsWith('"')) || (s.StartsWith('\'') && s.EndsWith('\''))
            ? s[1..^1] : s;
}
