﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.AzureAppServices.FunctionalTests
{
    public class TestCommand
    {
        private string _dotnetPath = GetDotnetPath();

        private static string GetDotnetPath()
        {
            var current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current != null)
            {
                var dotnetSubdir = new DirectoryInfo(Path.Combine(current.FullName, ".test-dotnet"));
                if (dotnetSubdir.Exists)
                {
                    var dotnetName = Path.Combine(dotnetSubdir.FullName, "dotnet.exe");
                    if (!File.Exists(dotnetName))
                    {
                        throw new InvalidOperationException("dotnet directory was found but dotnet.exe is not in it");
                    }
                    return dotnetName;
                }
                current = current.Parent;
            }

            throw new InvalidOperationException("dotnet executable was not found");
        }

        private List<string> _cliGeneratedEnvironmentVariables = new List<string> { "MSBuildSDKsPath" };

        protected string _command;

        public Process CurrentProcess { get; private set; }

        public Dictionary<string, string> Environment { get; } = new Dictionary<string, string>();

        public event DataReceivedEventHandler ErrorDataReceived;

        public event DataReceivedEventHandler OutputDataReceived;

        public string WorkingDirectory { get; set; }
        public ILogger Logger { get; set; }

        public TestCommand(string command)
        {
            _command = command;
        }

        public void KillTree()
        {
            if (CurrentProcess == null)
            {
                throw new InvalidOperationException("No process is available to be killed");
            }

            CurrentProcess.KillTree();
        }

        public virtual async Task<CommandResult> ExecuteAsync(string args = "")
        {
            var resolvedCommand = _command;

            ResolveCommand(ref resolvedCommand, ref args);

            Logger.LogInformation($"Executing - {resolvedCommand} {args} - {WorkingDirectoryInfo()}");

            return await ExecuteAsyncInternal(resolvedCommand, args);
        }

        private async Task<CommandResult> ExecuteAsyncInternal(string executable, string args)
        {
            var stdOut = new List<String>();

            var stdErr = new List<String>();

            CurrentProcess = CreateProcess(executable, args);

            CurrentProcess.ErrorDataReceived += (s, e) =>
            {
                stdErr.Add(e.Data);

                var handler = ErrorDataReceived;

                if (handler != null)
                {
                    handler(s, e);
                }
            };

            CurrentProcess.OutputDataReceived += (s, e) =>
            {
                stdOut.Add(e.Data);

                var handler = OutputDataReceived;

                if (handler != null)
                {
                    handler(s, e);
                }
            };

            var completionTask = StartAndWaitForExitAsync(CurrentProcess);

            CurrentProcess.BeginOutputReadLine();

            CurrentProcess.BeginErrorReadLine();

            await completionTask;

            CurrentProcess.WaitForExit();

            RemoveNullTerminator(stdOut);

            RemoveNullTerminator(stdErr);

            var stdOutString = String.Join(System.Environment.NewLine, stdOut);
            var stdErrString = String.Join(System.Environment.NewLine, stdErr);


            if (!string.IsNullOrWhiteSpace(stdOutString))
            {
                Logger.LogInformation("stdout: {out}", stdOutString);
            }

            if (!string.IsNullOrWhiteSpace(stdErrString))
            {
                Logger.LogInformation("stderr: {err}", stdErrString);
            }

            return new CommandResult(
                CurrentProcess.StartInfo,
                CurrentProcess.ExitCode,
                stdOutString,
                stdErrString);
        }

        private Process CreateProcess(string executable, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                UseShellExecute = false
            };

            RemoveCliGeneratedEnvironmentVariablesFrom(psi);

            AddEnvironmentVariablesTo(psi);

            AddWorkingDirectoryTo(psi);

            var process = new Process
            {
                StartInfo = psi
            };

            process.EnableRaisingEvents = true;

            return process;
        }

        private string WorkingDirectoryInfo()
        {
            if (WorkingDirectory == null)
            {
                return "";
            }

            return $" in {WorkingDirectory}";
        }

        private void RemoveNullTerminator(List<string> strings)
        {
            var count = strings.Count;

            if (count < 1)
            {
                return;
            }

            if (strings[count - 1] == null)
            {
                strings.RemoveAt(count - 1);
            }
        }

        private void ResolveCommand(ref string executable, ref string args)
        {
            if (executable == "dotnet")
            {
                executable = _dotnetPath;
                return;
            }

            throw new ArgumentOutOfRangeException(nameof(executable));
        }

        private void RemoveCliGeneratedEnvironmentVariablesFrom(ProcessStartInfo psi)
        {
            foreach (var name in _cliGeneratedEnvironmentVariables)
            {
                psi.Environment.Remove(name);
            }
        }

        private void AddEnvironmentVariablesTo(ProcessStartInfo psi)
        {
            foreach (var item in Environment)
            {
                psi.Environment[item.Key] = item.Value;
            }
        }

        private void AddWorkingDirectoryTo(ProcessStartInfo psi)
        {
            if (!string.IsNullOrWhiteSpace(WorkingDirectory))
            {
                psi.WorkingDirectory = WorkingDirectory;
            }
        }
        public static Task StartAndWaitForExitAsync(Process subject)
        {
            var taskCompletionSource = new TaskCompletionSource<object>();

            subject.EnableRaisingEvents = true;

            subject.Exited += (s, a) =>
            {
                taskCompletionSource.SetResult(null);
            };

            subject.Start();

            return taskCompletionSource.Task;
        }
    }
}
