﻿// Copyright 2016 Google Inc. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Xunit;

namespace Google.Cloud.Metadata.V1.IntegrationTests
{
    /// <summary>
    /// Base class for a fixture which is set up at the start of the test run, and torn down at the end.
    /// </summary>
    public class MetadataFixtureBase : IDisposable
    {
        private const string EmulatorEnvironmentVariable = "METADATA_EMULATOR_HOST";

        private readonly StringBuilder emulatorErrorOutput = new StringBuilder();
        private readonly StringBuilder emulatorOutput = new StringBuilder();
        private readonly Process emulatorProcess;
        private readonly string emulatorPath;

        protected MetadataFixtureBase()
        {
            var typeInfo = typeof(MetadataFixtureBase).GetTypeInfo();

            emulatorPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emulatorPath);

            Func<string, string> copyResource = name =>
            {
                var destination = Path.Combine(emulatorPath, name);
                using (var input = typeInfo.Assembly.GetManifestResourceStream($"{typeInfo.Namespace}.Emulator.{name}"))
                using (var output = File.Create(destination))
                {
                    input.CopyTo(output);
                    return destination;
                }
            };

            copyResource("common.py");
            copyResource("initial_data.json");
            var emulatorFilePath = copyResource("metadata_server.py");

            var temp = new TcpListener(IPAddress.Loopback, 0);
            temp.Start();
            int port = ((IPEndPoint)temp.LocalEndpoint).Port;
            temp.Stop();

            Environment.SetEnvironmentVariable(EmulatorEnvironmentVariable, $"localhost:{port}");

            var startInfo = new ProcessStartInfo("python", $"{emulatorFilePath} --test --port {port}");
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.UseShellExecute = false;
            emulatorProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            emulatorProcess.Exited += EmulatorProcess_Exited;
            emulatorProcess.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    emulatorOutput.AppendLine(e.Data);
                }
            };
            emulatorProcess.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    emulatorErrorOutput.AppendLine(e.Data);
                }
            };
            emulatorProcess.Start();
            emulatorProcess.BeginOutputReadLine();
            emulatorProcess.BeginErrorReadLine();
        }

        private void EmulatorProcess_Exited(object sender, EventArgs e)
        {
            var message = $"The metadata server emulator exited with code {emulatorProcess.ExitCode}";

            var output = emulatorOutput.ToString();
            if (!string.IsNullOrEmpty(output))
            {
                message += $"\nOutput:\n-------\n{output}\n--------------------";
            }
            var error = emulatorErrorOutput.ToString();
            if (!string.IsNullOrEmpty(error))
            {
                message += $"\nError Output:\n-------------\n{error}\n--------------------";
            }
            throw new InvalidOperationException(message);
        }

        public void Dispose()
        {
            emulatorProcess.Exited -= EmulatorProcess_Exited;
            emulatorProcess.Kill();

            Directory.Delete(emulatorPath, recursive: true);
        }
    }
}
