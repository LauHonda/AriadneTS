using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AriadneTS.Runtime;

internal static class Program
{
    private static int Main()
    {
        var logs = new List<string>();
        var modules = new Dictionary<string, string>
        {
            ["feature.js"] = "export const message = '你好 from managed host';",
        };
        var hostInvokeCount = 0;

        using (var runtime = new ScriptRuntime(
            logs.Add,
            name => modules.TryGetValue(name, out var source) ? source : null,
            hostInvoker: (method, payload) =>
            {
                ++hostInvokeCount;
                Require(method == "math.double", $"Unexpected host method: {method}");
                Require(payload == "{\"value\":21}", $"Unexpected host payload: {payload}");
                return "{\"value\":42}";
            }))
        {
            runtime.EvaluateModule(
                "import { message } from './feature.js';" +
                "host.log(message);" +
                "Promise.resolve().then(() => host.log('managed promise job'));",
                "bootstrap.js");

            var executedJobs = runtime.ExecutePendingJobs();
            Require(executedJobs > 0, "Expected at least one promise job.");

            runtime.EvaluateModule(
                "globalThis.__ariadnets_invoke = (method, payload) => {" +
                "  if (method === 'sum') return payload.left + payload.right;" +
                "  throw new Error(`unknown method: ${method}`);" +
                "};",
                "invoke-entry.js");
            var result = runtime.Invoke("sum", "{\"left\":19,\"right\":23}");
            Require(result == "42", $"Expected invoke result 42, got {result}.");

            runtime.EvaluateModule(
                "globalThis.__ariadnets_invoke = () => " +
                "host.invoke('math.double', { value: 21 }).value;",
                "host-invoke-entry.js");
            result = runtime.Invoke("hostInvoke");
            Require(result == "42", $"Expected host invoke result 42, got {result}.");
            Require(hostInvokeCount == 1, $"Expected one host handler call, got {hostInvokeCount}.");
        }

        Require(logs.Count == 2, $"Expected two logs, got {logs.Count}.");
        Require(logs[0] == "你好 from managed host", "UTF-8 module log did not match.");
        Require(logs[1] == "managed promise job", "Promise log did not match.");

        try
        {
            using var runtime = new ScriptRuntime(
                _ => { },
                _ => throw new InvalidOperationException("expected managed loader failure"));
            runtime.EvaluateModule("import './failure.js';", "failure-entry.js");
            throw new InvalidOperationException("Expected the managed loader failure to surface.");
        }
        catch (ScriptRuntimeException exception)
        {
            Require(
                exception.Status == "HostCallbackError",
                $"Expected HostCallbackError, got {exception.Status}.");
            Require(
                exception.Message == "expected managed loader failure",
                "Managed loader exception message did not match.");
        }

        try
        {
            using var runtime = new ScriptRuntime(
                _ => { },
                hostInvoker: (_, _) => throw new InvalidOperationException("expected host invoke failure"));
            runtime.Evaluate("host.invoke('failure', null);");
            throw new InvalidOperationException("Expected the host invoke failure to surface.");
        }
        catch (ScriptRuntimeException exception)
        {
            Require(
                exception.Status == "HostCallbackError",
                $"Expected HostCallbackError, got {exception.Status}.");
            Require(
                exception.Message == "expected host invoke failure",
                "Host invoke exception message did not match.");
        }

        var typeScriptDist = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../TypeScript/dist"));
        var typeScriptModules = new DirectoryModuleSource(typeScriptDist);
        using (var runtime = new ScriptRuntime(
            logs.Add,
            typeScriptModules.Load,
            hostInvoker: (method, payload) =>
            {
                Require(method == "demo.getPlayer", $"Unexpected demo host method: {method}");
                Require(
                    payload == "{\"requestedBy\":\"TypeScript\"}",
                    $"Unexpected demo host payload: {payload}");
                return "{\"name\":\"Ariadne\",\"engine\":\"ManagedTests\"}";
            }))
        {
            var entry = typeScriptModules.Load("bootstrap.js");
            Require(entry != null, "Compiled TypeScript bootstrap module was not found.");
            runtime.EvaluateModule(entry, "bootstrap.js");
            runtime.Invoke("start");
            runtime.Invoke("update", "{\"deltaTime\":1.25}");
            runtime.ExecutePendingJobs();

            var greeting = runtime.Invoke("demo.greet", "{\"message\":\"Hello from C#\"}");
            Require(
                greeting == "{\"reply\":\"Hello from TypeScript, Hello from C#\"}",
                $"Unexpected TypeScript demo result: {greeting}");

            var state = runtime.Invoke("beforeReload");
            Require(
                state == "{\"elapsedSeconds\":1.25}",
                $"Unexpected TypeScript reload state: {state}");
            runtime.Invoke("shutdown");
        }

        TestScriptPackageReader();

        Console.WriteLine("managed runtime smoke test passed");
        return 0;
    }

    private static void TestScriptPackageReader()
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var publicKey = $"RSA1.{EncodeBase64Url(parameters.Modulus)}.{EncodeBase64Url(parameters.Exponent)}";
        var modules = new Dictionary<string, string>
        {
            ["bootstrap.js"] = "import { value } from './feature.js'; host.log(value);",
            ["feature.js"] = "export const value = 'package module';",
        };
        var packageBytes = CreatePackageBytes(rsa, "signed-test", 1, modules);
        var reader = new ScriptPackageReader(
            publicKey,
            bytes => JsonSerializer.Deserialize<ScriptPackageManifest>(
                bytes,
                new JsonSerializerOptions { IncludeFields = true }));
        var package = reader.Read(packageBytes);
        Require(package.Manifest.Version == "signed-test", "Package version did not match.");
        Require(package.LoadModule("feature.js") == modules["feature.js"], "Package module did not match.");
        Require(package.LoadModule("missing.js") == null, "Missing package module should return null.");

        using (var runtime = new ScriptRuntime(_ => { }, package.LoadModule))
        {
            runtime.EvaluateModule(package.LoadModule(package.Manifest.EntryModule), package.Manifest.EntryModule);
        }

        packageBytes[packageBytes.Length - 1] ^= 0x01;
        try
        {
            reader.Read(packageBytes);
            throw new InvalidOperationException("Expected tampered package to fail.");
        }
        catch (InvalidDataException)
        {
        }
    }

    private static byte[] CreatePackageBytes(
        RSA rsa,
        string version,
        long buildNumber,
        IReadOnlyDictionary<string, string> modules)
    {
        var files = new List<ScriptPackageFile>();
        foreach (var module in modules)
        {
            var data = Encoding.UTF8.GetBytes(module.Value);
            files.Add(new ScriptPackageFile
            {
                Path = module.Key,
                SizeBytes = data.Length,
                Sha256 = ComputeSha256(data),
            });
        }

        var manifest = new ScriptPackageManifest
        {
            Version = version,
            BuildNumber = buildNumber,
            RequiredRuntimeAbiVersion = ScriptRuntime.RequiredAbiVersion,
            EntryModule = "bootstrap.js",
            Files = files.ToArray(),
        };
        var options = new JsonSerializerOptions { IncludeFields = true };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, options);
        var signature = rsa.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Encoding.ASCII.GetBytes("ARDPKG01"));
        writer.Write((uint)1);
        writer.Write((uint)manifestBytes.Length);
        writer.Write((uint)signature.Length);
        writer.Write((uint)modules.Count);
        writer.Write(manifestBytes);
        writer.Write(signature);
        foreach (var module in modules)
        {
            var path = Encoding.UTF8.GetBytes(module.Key);
            var data = Encoding.UTF8.GetBytes(module.Value);
            writer.Write((uint)path.Length);
            writer.Write((ulong)data.Length);
            writer.Write(path);
            writer.Write(data);
        }
        return stream.ToArray();
    }

    private static string ComputeSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(data))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static string EncodeBase64Url(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
