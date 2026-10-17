using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Shared.Config;

namespace ServerPlugin.AutoCommands;

/// <summary>
/// A live instance of an <see cref="AutoCommand"/>'s step list. Advanced once per
/// frame by <see cref="AutoCommandExecutor"/> on the game thread. For each step it
/// optionally runs a shell script first (waiting for it to exit without blocking
/// the server), then runs the step action, then waits the step's delay before
/// moving on. Several sequences may run at once (e.g. a countdown chaining others
/// via the RunAuto action).
/// </summary>
internal sealed class RunningSequence
{
    private readonly AutoCommandExecutor executor;
    private readonly List<CommandStep> steps;

    private int index;
    private DateTime dueAt;

    private bool shellRunning;
    private DateTime shellStartedAt;
    private TimeSpan shellTimeout;
    private Process shellProcess;

    public string Name { get; }
    public bool Completed { get; private set; }

    public RunningSequence(AutoCommandExecutor executor, AutoCommand command, DateTime now)
    {
        this.executor = executor;
        Name = command.Name ?? "";
        steps = command.Steps ?? new List<CommandStep>();
        dueAt = now; // the first step runs immediately
        if (steps.Count == 0)
            Completed = true;
    }

    public void Tick(DateTime now)
    {
        if (Completed)
            return;

        if (index >= steps.Count)
        {
            Completed = true;
            return;
        }

        if (shellRunning)
        {
            if (!ShellFinished(now))
                return; // keep waiting across frames without blocking the server
            DisposeShell();
            RunActionAndAdvance(now);
            return;
        }

        if (now < dueAt)
            return;

        CommandStep step = steps[index];
        if (!string.IsNullOrWhiteSpace(step.ShellScript) && TryStartShell(step, now))
            return; // wait for the shell to finish; the action runs once it exits

        RunActionAndAdvance(now);
    }

    /// <summary>Stops the sequence; used when the executor aborts it on error.</summary>
    public void ForceComplete()
    {
        DisposeShell();
        Completed = true;
    }

    private void RunActionAndAdvance(DateTime now)
    {
        CommandStep step = steps[index];
        try
        {
            executor.RunStepAction(step);
        }
        catch (Exception e)
        {
            executor.Log.Error(e, "Auto command '{0}' step {1} failed", Name, index);
        }

        dueAt = now + ParseSpan(step.Delay);
        index++;
        if (index >= steps.Count)
            Completed = true;
    }

    private bool ShellFinished(DateTime now)
    {
        bool exited;
        try
        {
            exited = shellProcess == null || shellProcess.HasExited;
        }
        catch
        {
            exited = true;
        }

        if (exited)
            return true;

        if (shellTimeout > TimeSpan.Zero && now - shellStartedAt >= shellTimeout)
        {
            executor.Log.Warning(
                "Auto command '{0}' step {1}: shell script timed out",
                Name,
                index
            );
            try
            {
                shellProcess?.Kill();
            }
            catch
            {
                // ignored
            }

            return true;
        }

        return false;
    }

    private bool TryStartShell(CommandStep step, DateTime now)
    {
        try
        {
            var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c " + step.ShellScript;
            }
            else
            {
                psi.FileName = "/bin/sh";
                psi.Arguments = "-c " + ShQuote(step.ShellScript);
            }

            shellProcess = Process.Start(psi);
            if (shellProcess == null)
                return false;

            shellRunning = true;
            shellStartedAt = now;
            shellTimeout =
                step.ShellTimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(step.ShellTimeoutSeconds)
                    : TimeSpan.Zero; // 0 = wait indefinitely
            return true;
        }
        catch (Exception e)
        {
            executor.Log.Error(
                e,
                "Auto command '{0}' step {1}: failed to start shell script",
                Name,
                index
            );
            DisposeShell();
            return false;
        }
    }

    private void DisposeShell()
    {
        try
        {
            shellProcess?.Dispose();
        }
        catch
        {
            // ignored
        }

        shellProcess = null;
        shellRunning = false;
    }

    private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private static TimeSpan ParseSpan(string text) =>
        TimeSpan.TryParse(text, out TimeSpan span) && span > TimeSpan.Zero ? span : TimeSpan.Zero;
}
