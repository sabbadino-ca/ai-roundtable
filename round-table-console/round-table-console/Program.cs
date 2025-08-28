// Program.cs  –  .NET 8   (console-multiplexer with per-child ShowWindow flag)
using round_table_console;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/*───────────────────────────────────────── 1 - Data types ─────────────────────────────────────────*/



record ChildSpec(string Name, string Cmd, string args);

public enum MsgKind { User, Llm }

public class LogEntry
{
    public DateTime Timestamp { get;init; }
    public string Source { get;init; }
public string Message { get; init; }
       public MsgKind Kind { get; init; }
}

public class LogEntries
{
    public long Index { get; init; }
    public List<LogEntry> Entries { get; init; } = new();
}
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
    private static  string _nameOfRoundTableParticipant = "master of the universe";
    // global state
    private static readonly List<LogEntries> _log = new();
    private static int _currentConversationPair =-1;

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
                _currentConversationPair++;
                var entry = new LogEntries { Index = _currentConversationPair };
                _log.Add(entry);
                if (TryParseTarget(line, out var tgt, out var msg))
                {
                    Append(_nameOfRoundTableParticipant, msg, MsgKind.User);
                    if (_children.TryGetValue(tgt, out var child) && !child.Proc.HasExited)
                    {
                        await SendWithCatchUpAsync(msg, child);


                    }
                    else
                    {
                        _log.RemoveAt(_log.Count - 1);  
                        _currentConversationPair--;
                        Console.Error.WriteLine($"[proxy] child \"{tgt}\" not found or exited");
                    }
                }
                else
                {
                    Append(_nameOfRoundTableParticipant, line, MsgKind.User);
                    foreach (var c in _children.Values)
                        if (!c.Proc.HasExited)
                            await SendWithCatchUpAsync(line, c);
                    
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
                          new JsonSerializerOptions {
                              PropertyNameCaseInsensitive = true,
                              ReadCommentHandling = JsonCommentHandling.Skip
                          }) ?? [];
            var dupe = specs.GroupBy(s => s.Name).FirstOrDefault(g => g.Count() > 1);
            if (dupe != null) { Console.Error.WriteLine($"Duplicate child name \"{dupe.Key}\""); return false; }
            foreach (var s in specs)
            {
             await Spawn(s);
            }
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Config error: {ex.Message}"); return false; }
    }

    private async static Task Spawn(ChildSpec spec)
    {
        var exe = spec.Cmd;
        var  argLine = spec.args;

        //var p = await CommandRunnerService.RunCommand(exe + " " + argLine);

        var psi = new ProcessStartInfo(exe, argLine)
        {
            WorkingDirectory = @"C:\\Users\\esabbadin\\AppData\\Local\\nvm\\v22.15.0\\node_modules\\@anthropic-ai\\claude-code",    
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            psi.WindowStyle = ProcessWindowStyle.Hidden;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!p.Start()) { Console.Error.WriteLine($"[{spec.Name}] FAILED to start {spec.Cmd}"); return; }

        var child = new Child { Name = spec.Name, Proc = p };
        child.StdOutPump = Pump(child, p.StandardOutput, Console.Out);
        child.StdErrPump = Pump(child, p.StandardError, Console.Error, ConsoleColor.Red);

        _children.TryAdd(spec.Name, child);
        Console.WriteLine($"Spawned [{spec.Name}] {spec.Cmd}");
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
            Append(child.Name, line, MsgKind.Llm);
            _cursor[child.Name] = _currentConversationPair;

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

    private static void Append(string src, string msg, MsgKind kind)
    {
        var last = _log.Last();
        last.Entries.Add(new LogEntry { Timestamp= DateTime.UtcNow,  Source= src,  Message = msg, Kind = kind });
    }

    /*───────────────────────────────────────── 5 - Catch-up sender (escaped \n) ───────────────────────*/

    private static async Task SendWithCatchUpAsync(string userText, Child target)
    {
        if (_currentConversationPair==0)
        {
            string payloadb = $"{_nameOfRoundTableParticipant}: {Escape(userText)}";
            await target.Proc.StandardInput.WriteLineAsync(payloadb);
            return;
        }
        long last = _cursor.TryGetValue(target.Name, out var i) ? i : 0;
        //_log.Count-1 : ignore the last entry, which is the current active conversation 
        var logWithoutCurrent = _log.Take(_log.Count - 1);
        var missed = logWithoutCurrent.Where(e => e.Index > last).OrderBy(e => e.Index).ToList();
        if (missed.Count == 0)
        {
            // send only previous utterance 
            var tt = logWithoutCurrent.Last().Entries.Where(x => x.Source != target.Name);
            string msg1= "";
            // previous conversation utterance had no other actors 
            if (tt.Count()>1) // ==1 BECAUSE IF THE USER 
            {
                msg1 += string.Join(' ', tt.Select(e => $"<{e.Source}: {Escape(e.Message)} />"));
            }
            msg1 = $"{msg1} - {_nameOfRoundTableParticipant}: {Escape(userText)}";
            await target.Proc.StandardInput.WriteLineAsync(msg1);
        }
        else
        {
            string msg2 = "";
            foreach (var miss in missed)
            {
                var tt = miss.Entries.Where(x => x.Source != target.Name);
                if (tt.Count() >1) // ==1 BECAUSE OF THE USER 
                {
                    msg2 += string.Join(' ', tt.Select(e => $"<{e.Source}: {Escape(e.Message)} />"));
                }
            }
            msg2= $"{msg2} - {_nameOfRoundTableParticipant}: {Escape(userText)}";
            await target.Proc.StandardInput.WriteLineAsync(msg2);
        }
        _cursor[target.Name] = _currentConversationPair ;            // mark as caught up
    }
    private static string Escape(string s) => s.Replace("\r", "").Replace("\n", "\\n");

    /*───────────────────────────────────────── 6 - Parsing helpers ───────────────────────────────────*/

    private static bool TryParseTarget(string line, out string name, out string payload)
    {
        // Split on first ':'; require at least one non-colon, non-whitespace char before ':'
        var m = Regex.Match(line, @"^\s*([^:\s].*?)\s*:\s*(.*)$");
        if (m.Success) { name = m.Groups[1].Value; payload = m.Groups[2].Value; return true; }
        name = payload = ""; return false;
    }

  
  
}
