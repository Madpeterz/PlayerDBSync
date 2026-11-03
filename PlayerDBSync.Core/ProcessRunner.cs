using System.ComponentModel;
using System.Diagnostics;

namespace PlayerDBSync
{
    internal sealed record ProcessResult(int ExitCode, byte[] Output, string Error);

    /// <summary>Runs a program and collects what it prints; here so what needs one (the X11 capture, the trash) can be tested without it.</summary>
    internal interface IProcessRunner
    {
        /// <summary>The program's result, or null if it couldn't be started (not installed) or didn't finish in time.</summary>
        ProcessResult? Run(string program, IReadOnlyList<string> arguments, TimeSpan timeout);
    }

    internal sealed class ProcessRunner : IProcessRunner
    {
        public static readonly IProcessRunner Default = new ProcessRunner();

        public ProcessResult? Run(string program, IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            var start = new ProcessStartInfo(program)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            Process? process;
            try
            {
                process = Process.Start(start);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                return null;
            }
            if (process == null)
            {
                return null;
            }

            using (process)
            {
                process.StandardInput.Close();
                var output = new MemoryStream();
                var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeout))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                    {
                    }
                    return null;
                }
                // Exited, so both pipes end; a grandchild holding one open is waited for only briefly.
                Task.WaitAll([copy, error], TimeSpan.FromSeconds(5));
                return new ProcessResult(process.ExitCode, output.ToArray(), error.IsCompletedSuccessfully ? error.Result : "");
            }
        }
    }
}
