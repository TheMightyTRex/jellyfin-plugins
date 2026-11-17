using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AudioDescription;

/// <summary>What a program said when it ended.</summary>
public sealed record RunResult(int ExitCode, string Output, string Errors);

/// <summary>Runs Jellyfin's own ffprobe and ffmpeg. Replaced in the tests.</summary>
public interface IRunner
{
    Task<RunResult> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Runs a program with its arguments passed one by one (never through a shell), at low priority.</summary>
public sealed class ProcessRunner : IRunner
{
    public async Task<RunResult> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in arguments)
        {
            info.ArgumentList.Add(a);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException(program + " did not start");
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            // It still runs, at the normal priority.
        }

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException(program + " took longer than " + timeout.TotalMinutes + " minutes");
        }

        return new RunResult(process.ExitCode, await output.ConfigureAwait(false), await errors.ConfigureAwait(false));
    }
}
