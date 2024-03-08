// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

using System.IO.Pipes;
using Microsoft.Build.Framework;

namespace Microsoft.NET.Sdk.Testing.Tasks
{
    public class GetTestsProject : Build.Utilities.Task
    {
        [Required]

        public ITaskItem TargetPath { get; set; }

        [Required]

        public ITaskItem GetTestsProjectPipeName { get; set; }

        public override bool Execute()
        {
            Log.LogMessage(MessageImportance.Low, $"Target path: {TargetPath}");
            using NamedPipeClientStream namedPipeClientStream = new(".", GetTestsProjectPipeName.ItemSpec, PipeDirection.InOut);
            namedPipeClientStream.Connect();
            using StreamWriter streamWriter = new(namedPipeClientStream);
            streamWriter.WriteLine(TargetPath.ItemSpec);
            return true;
        }
    }
}
