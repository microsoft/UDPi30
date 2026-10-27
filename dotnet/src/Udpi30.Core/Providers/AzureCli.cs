using System.Diagnostics;

namespace Udpi30.Core.Providers;

/// <summary>Raised when an <c>az</c> CLI invocation fails.</summary>
public sealed class AzureCliError(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
    public override string ToString() => $"az CLI error ({ExitCode}): {Message}";
}

/// <summary>Result of an <c>az</c> invocation.</summary>
public readonly record struct AzCliResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Thin wrapper over the Azure CLI (<c>az</c>). The Azure provider uses
/// <c>az deployment</c> with <c>--template-file *.bicep</c>, which compiles Bicep
/// in-process, so no separate Bicep toolchain is required beyond a logged-in
/// <c>az</c>. On Windows <c>az</c> is a batch script, so it is launched through
/// <c>cmd /c</c>; elsewhere it is launched directly.
/// </summary>
public class AzureCli
{
    /// <summary>Run an <c>az</c> command. Does not throw on non-zero exit.</summary>
    public virtual AzCliResult Run(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("az");
        }
        else
        {
            psi.FileName = "az";
        }
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)
            ?? throw new AzureCliError(-1, "could not start 'az' — is the Azure CLI installed and on PATH?");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return new AzCliResult(proc.ExitCode, stdout, stderr);
    }

    /// <summary>Run an <c>az</c> command, throwing <see cref="AzureCliError"/> on failure.</summary>
    public AzCliResult RunChecked(IReadOnlyList<string> args)
    {
        var result = Run(args);
        if (!result.Ok)
        {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new AzureCliError(result.ExitCode, $"az {string.Join(' ', args)}\n{detail.Trim()}");
        }
        return result;
    }
}
