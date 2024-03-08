// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*
cd C:\git\sdk\src\Cli\dotnet
dotnet build
Copy-Item C:\git\sdk\artifacts\bin\Microsoft.NET.Sdk.Testing.Tasks\Debug\net9.0\Microsoft.NET.Sdk.Testing.Tasks.dll -Destination C:\git\sdk\artifacts\bin\redist\Debug\dotnet\sdk\9.0.100-dev
Copy-Item C:\git\sdk\artifacts\bin\dotnet\Debug\net9.0\dotnet.dll -Destination  C:\git\sdk\artifacts\bin\redist\Debug\dotnet\sdk\9.0.100-dev

C:\git\localPlayground\garbage\dotnettest>
dotnet msbuild -t:GetTestsProject /bl
dotnet --debug test 
*/

using System.Buffers;
using System.CommandLine;
using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Tools.Build;

namespace Microsoft.DotNet.Tools.Test
{
    internal class TestingPlatform
    {
        private const string MSBuildExeName = "MSBuild.dll";
        private static readonly string _pipeName = Guid.NewGuid().ToString("N");
        private static List<NamedPipeServerStream> s_namedPipeServerStreams = new();
        private static Task _loopTask;
        private static List<Task> s_taskModuleName = new();
        private static List<Task> s_testsRun = new();

        public static int Run(ParseResult parseResult)
        {
            CancellationTokenSource cancellationTokenSource = new();
            _loopTask = Task.Run(async () => await WaitConnectionAsync(cancellationTokenSource.Token));

            if (parseResult.UnmatchedTokens.Count(x => x == "--no-build") == 0)
            {
                BuildCommand buildCommand = BuildCommand.FromArgs(["-t:Build;_GetTestsProject", $"-p:GetTestsProjectPipeName={_pipeName}"]);
                int buildResult = buildCommand.Execute();
            }
            else
            {
                ForwardingAppImplementation mSBuildForwardingApp = new(GetMSBuildExePath(), ["-t:_GetTestsProject", $"-p:GetTestsProjectPipeName={_pipeName}", "-verbosity:q"]);
                int getTestsProjectResult = mSBuildForwardingApp.Execute();
            }

            // Above line will block till we have all connections and all GetTestsProject msbuild task complete.
            Task.WaitAll([.. s_taskModuleName]);
            Task.WaitAll([.. s_testsRun]);
            cancellationTokenSource.Cancel();
            _loopTask.Wait();

            return 0;
        }

        private static async Task WaitConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    NamedPipeServerStream namedPipeServerStream = new(_pipeName, PipeDirection.InOut, maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances);
                    await namedPipeServerStream.WaitForConnectionAsync(cancellationToken);
                    s_namedPipeServerStreams.Add(namedPipeServerStream);
                    s_taskModuleName.Add(Task.Run(async () => await GetTestModuleName(namedPipeServerStream)));
                }
            }
            catch (OperationCanceledException)
            {
                // Fine we're closing
            }
        }

        private static async Task GetTestModuleName(NamedPipeServerStream namedPipeServerStream)
        {
            int bufferSize = 4096;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                MemoryStream messageBuffer = new();
                Memory<byte> readBuffer = new(buffer, 0, bufferSize);
                int byteRead;
                while ((byteRead = await namedPipeServerStream.ReadAsync(readBuffer)) > 0)
                {
                    await messageBuffer.WriteAsync(buffer, 0, byteRead);
                }

                string moduleName = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                s_testsRun.Add(Task.Run(async () => await RunTest(moduleName.Trim())));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static async Task RunTest(string module)
        {
            if (!File.Exists(module))
            {
                LockedConsoleWrite($"Test module '{module}' not found. Build the test application before or run 'dotnet test'.", ConsoleColor.Yellow);
                return;
            }

            ProcessStartInfo processStartInfo = new();
            if (module.EndsWith(".dll"))
            {
                processStartInfo.FileName = Environment.ProcessPath;
                processStartInfo.Arguments = $"exec {module}";
            }
            else
            {
                processStartInfo.FileName = module;
            }

            await Process.Start(processStartInfo).WaitForExitAsync();
        }

        private static string GetMSBuildExePath()
        {
            return Path.Combine(
                AppContext.BaseDirectory,
                MSBuildExeName);
        }

        private static void LockedConsoleWrite(string message, ConsoleColor consoleColor)
        {
            lock (MSBuildExeName)
            {
                ConsoleColor currentColor = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = consoleColor;
                    Console.WriteLine(message);
                }
                finally
                {
                    Console.ForegroundColor = currentColor;
                }
            }
        }
    }
}
