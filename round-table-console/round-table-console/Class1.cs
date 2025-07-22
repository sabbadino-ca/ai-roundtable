using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace round_table_console
{
    public static  class CommandRunnerService
    {
        public static async Task<Process> RunCommand(string commandToRun)
        {
            var workingDirectory = Directory.GetCurrentDirectory();

            var processStartInfo = new ProcessStartInfo()
            {
                FileName = "cmd",
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory
            };
            var process = new Process { StartInfo = processStartInfo, EnableRaisingEvents = true };
            if (!process.Start()) { 
                throw new Exception( $"[FAILED to start {commandToRun}"); 
            }
            process.StandardInput.WriteLine($"{commandToRun} {Environment.NewLine}");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            return process;
        }
    }
}
