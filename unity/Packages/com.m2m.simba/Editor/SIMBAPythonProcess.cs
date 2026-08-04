#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace M2M.SIMBA.Editor
{
    internal sealed class SIMBAProcessResult
    {
        public int ExitCode;
        public string StandardOutput = string.Empty;
        public string StandardError = string.Empty;
        public bool Success => ExitCode == 0;
    }

    internal static class SIMBAPythonProcess
    {
        public static string PackageRoot => PackageManagerPackageInfo.FindForAssembly(typeof(SIMBAPythonProcess).Assembly)?.resolvedPath
            ?? throw new InvalidOperationException("Unable to resolve the SIMBA package path.");

        public static string PythonTool(string fileName) => Path.Combine(PackageRoot, "Python~", fileName);

        public static SIMBAProcessResult Run(string executable, params string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(executable)) throw new ArgumentException("Python interpreter is not configured.");
            var info = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.Combine(PackageRoot, "Python~")
            };
            info.Arguments = string.Join(" ", Array.ConvertAll(arguments, QuoteArgument));
            using var process = new Process { StartInfo = info };
            if (!process.Start()) throw new InvalidOperationException("Unable to start Python.");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new SIMBAProcessResult { ExitCode = process.ExitCode, StandardOutput = stdout.Trim(), StandardError = stderr.Trim() };
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public static SIMBAProcessResult Validate(string executable) => Run(executable, "-c", "import sys, numpy, h5py; print(sys.version.split()[0]); print(numpy.__version__); print(h5py.__version__)");
    }
}
#endif
