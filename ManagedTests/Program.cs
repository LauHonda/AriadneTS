using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AriadneTS.Runtime;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--debug-adapter-runtime-fixture")
        {
            return RunDebugAdapterRuntimeFixture(args);
        }

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

        using (var runtime = new ScriptRuntime(
            _ => { },
            debugEnabled: true,
            debugHost: "127.0.0.1",
            debugPort: 39229))
        using (var client = new TcpClient())
        {
            client.Connect("127.0.0.1", 39229);
            using var stream = client.GetStream();
            var buffer = new byte[256];
            var read = stream.Read(buffer, 0, buffer.Length);
            var greeting = Encoding.UTF8.GetString(buffer, 0, read);
            Require(
                greeting.Contains("AriadneTS debug endpoint connected", StringComparison.Ordinal),
                "Debug endpoint greeting did not match.");
            Require(stream.Read(buffer, 0, buffer.Length) == 0, "Debug endpoint should close verification clients.");
        }

        using (var debugRuntimeReady = new ManualResetEventSlim(false))
        using (var startPausedScript = new ManualResetEventSlim(false))
        {
            const int checkpointDebugPort = 49330;
            logs.Clear();
            var pausedScript = Task.Run(() =>
            {
                using var runtime = new ScriptRuntime(
                    logs.Add,
                    debugEnabled: true,
                    debugHost: "127.0.0.1",
                    debugPort: checkpointDebugPort);
                debugRuntimeReady.Set();
                startPausedScript.Wait();
                runtime.Evaluate("globalThis.__ariadnets_debug_checkpoint('manual.js', 7, 0); host.log('after continue');");
            });

            Require(debugRuntimeReady.Wait(TimeSpan.FromSeconds(3)), "Debug runtime did not start.");
            Require(
                logs.Contains("AriadneTS debug endpoint is listening"),
                "Debug endpoint did not report listening.");
            Require(
                !logs.Contains("AriadneTS debug endpoint failed: thread creation failed"),
                "Debug endpoint thread creation failed.");
            var status = SendDebugCommand(checkpointDebugPort, "status\n");
            Require(status.Contains("\"state\":\"running\"", StringComparison.Ordinal), "Debug status should start as running.");
            startPausedScript.Set();
            Thread.Sleep(200);
            Require(!pausedScript.IsCompleted, "Debug checkpoint should pause script execution.");
            Require(
                logs.Exists(message => message.Contains("AriadneTS paused at manual.js:7:0", StringComparison.Ordinal)),
                "Debug checkpoint did not report paused location.");

            status = SendDebugCommand(checkpointDebugPort, "status\n");
            Require(status.Contains("\"state\":\"paused\"", StringComparison.Ordinal), "Debug status should report paused.");
            Require(status.Contains("\"module\":\"manual.js\"", StringComparison.Ordinal), "Debug status module did not match.");
            Require(status.Contains("\"line\":7", StringComparison.Ordinal), "Debug status line did not match.");

            var response = SendDebugCommand(checkpointDebugPort, "continue\n");
            Require(response.Contains("continued", StringComparison.Ordinal), "Debug continue response did not match.");
            Require(pausedScript.Wait(TimeSpan.FromSeconds(3)), "Debug checkpoint did not resume after continue.");
            Require(logs.Contains("after continue"), "Script did not continue after debug checkpoint.");
        }

        using (var debugRuntimeReady = new ManualResetEventSlim(false))
        using (var startPausedScript = new ManualResetEventSlim(false))
        {
            const int timeoutDebugPort = 49337;
            logs.Clear();
            var pausedScript = Task.Run(() =>
            {
                using var runtime = new ScriptRuntime(
                    logs.Add,
                    executionTimeoutMilliseconds: 100,
                    debugEnabled: true,
                    debugHost: "127.0.0.1",
                    debugPort: timeoutDebugPort);
                debugRuntimeReady.Set();
                startPausedScript.Wait();
                runtime.Evaluate("globalThis.__ariadnets_debug_checkpoint('timeout.js', 3, 0); host.log('after timeout-safe continue');");
            });

            Require(debugRuntimeReady.Wait(TimeSpan.FromSeconds(3)), "Timeout debug runtime did not start.");
            startPausedScript.Set();
            Thread.Sleep(300);
            Require(!pausedScript.IsCompleted, "Timeout debug checkpoint should pause script execution.");

            var response = SendDebugCommand(timeoutDebugPort, "{\"command\":\"continue\"}\n");
            Require(response.Contains("\"continued\":true", StringComparison.Ordinal), "Timeout debug continue response did not match.");
            Require(pausedScript.Wait(TimeSpan.FromSeconds(3)), "Debug pause consumed execution timeout after continue.");
            Require(logs.Contains("after timeout-safe continue"), "Script did not continue after timeout-safe debug pause.");
        }

        using (var debugRuntimeReady = new ManualResetEventSlim(false))
        using (var startDynamicScript = new ManualResetEventSlim(false))
        {
            const int dynamicDebugPort = 49331;
            logs.Clear();
            var dynamicScript = Task.Run(() =>
            {
                using var runtime = new ScriptRuntime(
                    logs.Add,
                    debugEnabled: true,
                    debugHost: "127.0.0.1",
                    debugPort: dynamicDebugPort);
                debugRuntimeReady.Set();
                startDynamicScript.Wait();
                runtime.Evaluate("globalThis.__ariadnets_debug_line('dynamic.ts', 5, 0); host.log('after dynamic continue');");
            });

            Require(debugRuntimeReady.Wait(TimeSpan.FromSeconds(3)), "Dynamic debug runtime did not start.");
            var response = SendDebugCommand(dynamicDebugPort, "break dynamic.ts:5\n");
            Require(response.Contains("breakpoint set", StringComparison.Ordinal), "Dynamic breakpoint was not set.");
            response = SendDebugCommand(dynamicDebugPort, "breakpoints\n");
            Require(response.Contains("\"module\":\"dynamic.ts\"", StringComparison.Ordinal), "Breakpoint list module did not match.");
            Require(response.Contains("\"line\":5", StringComparison.Ordinal), "Breakpoint list line did not match.");

            startDynamicScript.Set();
            Thread.Sleep(200);
            Require(!dynamicScript.IsCompleted, "Dynamic breakpoint should pause script execution.");

            var status = SendDebugCommand(dynamicDebugPort, "status\n");
            Require(status.Contains("\"state\":\"paused\"", StringComparison.Ordinal), "Dynamic status should report paused.");
            Require(status.Contains("\"module\":\"dynamic.ts\"", StringComparison.Ordinal), "Dynamic status module did not match.");

            response = SendDebugCommand(dynamicDebugPort, "continue\n");
            Require(response.Contains("continued", StringComparison.Ordinal), "Dynamic continue response did not match.");
            Require(dynamicScript.Wait(TimeSpan.FromSeconds(3)), "Dynamic breakpoint did not resume after continue.");
            Require(logs.Contains("after dynamic continue"), "Script did not continue after dynamic breakpoint.");
        }

        using (var debugRuntimeReady = new ManualResetEventSlim(false))
        using (var startLazyScript = new ManualResetEventSlim(false))
        {
            const int lazyDebugPort = 49338;
            logs.Clear();
            var lazyScript = Task.Run(() =>
            {
                using var runtime = new ScriptRuntime(
                    logs.Add,
                    debugEnabled: true,
                    debugHost: "127.0.0.1",
                    debugPort: lazyDebugPort);
                debugRuntimeReady.Set();
                startLazyScript.Wait();
                runtime.Evaluate(
                    "globalThis.__ariadnets_debug_line('lazy.ts', 4, 0, 'lazy', () => { throw new Error('snapshot should be lazy'); }, 'at lazy (lazy.ts:4:0)');" +
                    "const payload = { value: 42 };" +
                    "globalThis.__ariadnets_debug_line('lazy.ts', 5, 0, 'lazy', () => ({ payload }), 'at lazy (lazy.ts:5:0)');" +
                    "host.log('after lazy continue');");
            });

            Require(debugRuntimeReady.Wait(TimeSpan.FromSeconds(3)), "Lazy debug runtime did not start.");
            var response = SendDebugCommand(lazyDebugPort, "{\"command\":\"setBreakpoint\",\"module\":\"lazy.ts\",\"line\":5}\n");
            Require(response.Contains("\"ok\":true", StringComparison.Ordinal), "Lazy breakpoint was not set.");

            startLazyScript.Set();
            Thread.Sleep(200);
            Require(!lazyScript.IsCompleted, "Lazy breakpoint should pause script execution.");
            response = SendDebugCommand(lazyDebugPort, "{\"command\":\"variables\"}\n");
            Require(response.Contains("\"payload\"", StringComparison.Ordinal), "Lazy variable snapshot did not run at the active breakpoint.");
            Require(response.Contains("\"value\":42", StringComparison.Ordinal), "Lazy variable payload value is missing.");

            response = SendDebugCommand(lazyDebugPort, "{\"command\":\"continue\"}\n");
            Require(response.Contains("\"continued\":true", StringComparison.Ordinal), "Lazy continue response did not match.");
            Require(lazyScript.Wait(TimeSpan.FromSeconds(3)), "Lazy debug script did not finish after continue.");
            Require(logs.Contains("after lazy continue"), "Script did not continue after lazy breakpoint.");
        }

        using (var debugRuntimeReady = new ManualResetEventSlim(false))
        using (var startJsonScript = new ManualResetEventSlim(false))
        {
            const int jsonDebugPort = 49332;
            logs.Clear();
            var jsonScript = Task.Run(() =>
            {
                using var runtime = new ScriptRuntime(
                    logs.Add,
                    debugEnabled: true,
                    debugHost: "127.0.0.1",
                    debugPort: jsonDebugPort);
                debugRuntimeReady.Set();
                startJsonScript.Wait();
                runtime.Evaluate("globalThis.__ariadnets_debug_line('json.ts', 9, 0); host.log('after json continue');");
            });

            Require(debugRuntimeReady.Wait(TimeSpan.FromSeconds(3)), "JSON debug runtime did not start.");
            var response = SendDebugCommand(jsonDebugPort, "{\"command\":\"setBreakpoint\",\"module\":\"json.ts\",\"line\":9}\n");
            Require(response.Contains("\"ok\":true", StringComparison.Ordinal), "JSON breakpoint was not set.");
            response = SendDebugCommand(jsonDebugPort, "{\"command\":\"listBreakpoints\"}\n");
            Require(response.Contains("\"module\":\"json.ts\"", StringComparison.Ordinal), "JSON breakpoint list module did not match.");

            startJsonScript.Set();
            Thread.Sleep(200);
            Require(!jsonScript.IsCompleted, "JSON dynamic breakpoint should pause script execution.");

            var status = SendDebugCommand(jsonDebugPort, "{\"command\":\"status\"}\n");
            Require(status.Contains("\"state\":\"paused\"", StringComparison.Ordinal), "JSON status should report paused.");
            Require(status.Contains("\"module\":\"json.ts\"", StringComparison.Ordinal), "JSON status module did not match.");

            response = SendDebugCommand(jsonDebugPort, "{\"command\":\"continue\"}\n");
            Require(response.Contains("\"continued\":true", StringComparison.Ordinal), "JSON continue response did not match.");
            Require(jsonScript.Wait(TimeSpan.FromSeconds(3)), "JSON dynamic breakpoint did not resume after continue.");
            Require(logs.Contains("after json continue"), "Script did not continue after JSON breakpoint.");
        }

        RunDebugStepScenario(logs, 49333, 10, "{\"command\":\"next\"}\n", 11, "Step over did not stop after nested call.");
        RunDebugStepScenario(logs, 49334, 10, "{\"command\":\"stepIn\"}\n", 20, "Step in did not enter nested call.");
        RunDebugStepScenario(logs, 49335, 20, "{\"command\":\"stepOut\"}\n", 11, "Step out did not return to caller.");

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
                if (method == "ariadnets.log")
                {
                    return "null";
                }
                if (method == "ariadnets.async.begin")
                {
                    return "{\"ok\":true,\"result\":{\"requestId\":1}}";
                }
                if (method == "actors.create")
                {
                    return "{\"ok\":true,\"result\":{\"id\":1,\"name\":\"DemoPlayer\"}}";
                }
                if (method == "actors.setTransform" || method == "actors.setParent" || method == "actors.destroy")
                {
                    return "{\"ok\":true,\"result\":null}";
                }

                Require(method == "demo.getPlayer", $"Unexpected demo host method: {method}");
                Require(
                    payload == "{\"requestedBy\":\"TypeScript\"}",
                    $"Unexpected demo host payload: {payload}");
                return "{\"name\":\"Ariadne\",\"engine\":\"ManagedTests\"}";
            }))
        {
            var entry = typeScriptModules.Load("src/bootstrap.js");
            Require(entry != null, "Compiled TypeScript bootstrap module was not found.");
            runtime.EvaluateModule(entry, "src/bootstrap.js");
            runtime.Invoke("onBeginPlay");
            runtime.Invoke("onTick", "{\"deltaTime\":1.25}");
            runtime.ExecutePendingJobs();

            var greeting = runtime.Invoke("demo.greet", "{\"message\":\"Hello from C#\"}");
            Require(
                greeting == "{\"reply\":\"Hello from TypeScript, Hello from C#\"}",
                $"Unexpected TypeScript demo result: {greeting}");

            var state = runtime.Invoke("beforeReload");
            Require(
                state == "{\"elapsedSeconds\":1.25}",
                $"Unexpected TypeScript reload state: {state}");
            runtime.Invoke("onEndPlay");
        }

        TestScriptPackageReader();
        TestUnityProjectScriptPackage(logs);

        Console.WriteLine("managed runtime smoke test passed");
        return 0;
    }

    private static int RunDebugAdapterRuntimeFixture(string[] args)
    {
        var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort)
            ? parsedPort
            : 49391;
        var logs = new List<string>();
        using var runtime = new ScriptRuntime(
            logs.Add,
            debugEnabled: true,
            debugHost: "127.0.0.1",
            debugPort: (ushort)port);

        Console.WriteLine("READY");
        Console.Out.Flush();

        var command = Console.ReadLine();
        if (command != "RUN")
        {
            return 2;
        }

        runtime.Evaluate(
            "const payload = { deltaTime: 1.25 };" +
            "const circular = { name: 'loop' }; circular.self = '<circular>';" +
            "globalThis.__ariadnets_debug_line('src/game-application.ts', 87, 4, 'onTick', { payload, missing: '<undefined>', callback: '[Function tick]', big: '9007199254740993n', token: 'Symbol(token)', circular });" +
            "host.log('after first stop');" +
            "globalThis.__ariadnets_debug_line('src/game-application.ts', 88, 4, 'onTick', { payload });" +
            "host.log('after dap continue');",
            "dap-fixture.js");
        Require(logs.Contains("after dap continue"), "DAP fixture script did not continue.");
        Console.WriteLine("DONE");
        return 0;
    }

    private static void TestUnityProjectScriptPackage(List<string> logs)
    {
        var unityProjectDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../UnityProject"));
        var packagePath = Path.Combine(unityProjectDirectory, "Assets/typescript-package.bytes");
        var publicKeyPath = Path.Combine(unityProjectDirectory, "Assets/public-key.txt");
        if (!File.Exists(packagePath) || !File.Exists(publicKeyPath))
        {
            return;
        }

        var reader = new ScriptPackageReader(
            File.ReadAllText(publicKeyPath).Trim(),
            bytes => JsonSerializer.Deserialize<ScriptPackageManifest>(
                bytes,
                new JsonSerializerOptions { IncludeFields = true }));
        var package = reader.Read(File.ReadAllBytes(packagePath));
        Require(
            !string.IsNullOrEmpty(package.DebugMetadataJson),
            "Unity project package is missing debug metadata.");
        using (var debugMetadata = JsonDocument.Parse(package.DebugMetadataJson))
        {
            var root = debugMetadata.RootElement;
            Require(
                root.GetProperty("SchemaVersion").GetInt32() == 1,
                "Unexpected debug metadata schema version.");
            var hasMultilineProbe = false;
            foreach (var probe in root.GetProperty("Probes").EnumerateArray())
            {
                var module = probe.GetProperty("Module").GetString() ?? string.Empty;
                Require(
                    !module.StartsWith("ariadnets-sdk/", StringComparison.Ordinal),
                    "AriadneTS SDK files should not contain dynamic debug probes.");
                hasMultilineProbe |=
                    probe.GetProperty("Source").GetString() == "src/game-application.ts" &&
                    probe.GetProperty("Line").GetInt32() == 74;
            }
            Require(
                hasMultilineProbe,
                "Multiline statement probe is missing for game-application.ts:74.");
        }

        logs.Clear();
        using var runtime = new ScriptRuntime(
            logs.Add,
            package.LoadModule,
            hostInvoker: CreateDemoHostInvoker("UnityProjectPackage"));
        runtime.EvaluateModule(
            package.LoadModule(package.Manifest.EntryModule),
            package.Manifest.EntryModule);
        runtime.Invoke("onBeginPlay");
        runtime.Invoke("onTick", "{\"deltaTime\":1.25}");
        runtime.ExecutePendingJobs();
        var greeting = runtime.Invoke("demo.greet", "{\"message\":\"Hello from C#\"}");
        Require(
            greeting == "{\"reply\":\"Hello from TypeScript, Hello from C#\"}",
            $"Unexpected Unity project package result: {greeting}");
        runtime.Invoke("onEndPlay");
        TestUnityProjectPackageDebugging(package);
    }

    private static void TestUnityProjectPackageDebugging(ScriptPackage package)
    {
        const int debugPort = 49336;
        using var runtimeReady = new ManualResetEventSlim(false);
        using var invokeBeginPlay = new ManualResetEventSlim(false);
        using var beginPlayCompleted = new ManualResetEventSlim(false);
        using var invokeTick = new ManualResetEventSlim(false);
        var script = Task.Run(() =>
        {
            using var runtime = new ScriptRuntime(
                _ => { },
                package.LoadModule,
                hostInvoker: CreateDemoHostInvoker("UnityProjectDebug"),
                debugEnabled: true,
                debugHost: "127.0.0.1",
                debugPort: debugPort);
            runtime.EvaluateModule(
                package.LoadModule(package.Manifest.EntryModule),
                package.Manifest.EntryModule);
            runtimeReady.Set();
            invokeBeginPlay.Wait();
            runtime.Invoke("onBeginPlay");
            beginPlayCompleted.Set();
            invokeTick.Wait();
            runtime.Invoke("onTick", "{\"deltaTime\":1.25}");
            runtime.Invoke("onEndPlay");
        });

        Require(runtimeReady.Wait(TimeSpan.FromSeconds(5)), "Unity package debug runtime did not start.");
        var response = SendDebugCommand(
            debugPort,
            "{\"command\":\"setBreakpoint\",\"module\":\"src/game-application.ts\",\"line\":74}\n");
        Require(response.Contains("\"ok\":true", StringComparison.Ordinal), "Unity package multiline breakpoint was not set.");
        response = SendDebugCommand(
            debugPort,
            "{\"command\":\"setBreakpoint\",\"module\":\"src/game-application.ts\",\"line\":80}\n");
        Require(response.Contains("\"ok\":true", StringComparison.Ordinal), "Unity package post-initializer breakpoint was not set.");

        invokeBeginPlay.Set();
        Thread.Sleep(200);
        Require(!beginPlayCompleted.IsSet, "Unity package should pause at the multiline statement breakpoint.");
        response = SendDebugCommand(debugPort, "{\"command\":\"variables\"}\n");
        Require(response.Contains("\"this\"", StringComparison.Ordinal), "Unity package multiline breakpoint is missing this.");
        Require(response.Contains("\"player\"", StringComparison.Ordinal), "Unity package multiline breakpoint is missing player.");
        Require(!response.Contains("\"demoPlayer\"", StringComparison.Ordinal), "demoPlayer should not exist before its initializer runs.");

        response = SendDebugCommand(
            debugPort,
            "{\"command\":\"clearBreakpoint\",\"module\":\"src/game-application.ts\",\"line\":74}\n");
        Require(response.Contains("\"ok\":true", StringComparison.Ordinal), "Unity package multiline breakpoint was not cleared.");
        response = SendDebugCommand(debugPort, "{\"command\":\"continue\"}\n");
        Require(response.Contains("\"continued\":true", StringComparison.Ordinal), "Unity package multiline continue failed.");

        Thread.Sleep(200);
        Require(!beginPlayCompleted.IsSet, "Unity package should pause after assigning demoPlayer.");
        response = SendDebugCommand(debugPort, "{\"command\":\"variables\"}\n");
        Require(response.Contains("\"demoPlayer\"", StringComparison.Ordinal), "Unity package demoPlayer local is missing after assignment.");
        Require(response.Contains("\"name\":\"DemoPlayer\"", StringComparison.Ordinal), "Unity package demoPlayer value is missing.");
        Require(response.Contains("\"position\":{\"x\":1", StringComparison.Ordinal), "Unity package demoPlayer.position getter value is missing.");

        response = SendDebugCommand(
            debugPort,
            "{\"command\":\"clearBreakpoint\",\"module\":\"src/game-application.ts\",\"line\":80}\n");
        Require(response.Contains("\"ok\":true", StringComparison.Ordinal), "Unity package post-initializer breakpoint was not cleared.");
        response = SendDebugCommand(debugPort, "{\"command\":\"continue\"}\n");
        Require(response.Contains("\"continued\":true", StringComparison.Ordinal), "Unity package post-initializer continue failed.");
        Require(beginPlayCompleted.Wait(TimeSpan.FromSeconds(5)), "Unity package onBeginPlay did not finish.");

        response = SendDebugCommand(
            debugPort,
            "{\"command\":\"setBreakpoint\",\"module\":\"src/game-application.ts\",\"line\":87}\n");
        Require(response.Contains("\"ok\":true", StringComparison.Ordinal), "Unity package breakpoint was not set.");

        invokeTick.Set();
        Thread.Sleep(200);
        Require(!script.IsCompleted, "Unity package should pause at the onTick breakpoint.");

        response = SendDebugCommand(debugPort, "{\"command\":\"variables\"}\n");
        Require(response.Contains("\"payload\"", StringComparison.Ordinal), "Unity package payload local is missing.");
        Require(response.Contains("\"deltaTime\":1.25", StringComparison.Ordinal), "Unity package payload value is missing.");

        response = SendDebugCommand(debugPort, "{\"command\":\"next\"}\n");
        Require(response.Contains("\"continued\":true", StringComparison.Ordinal), "Unity package next command failed.");
        Thread.Sleep(200);
        var status = SendDebugCommand(debugPort, "{\"command\":\"status\"}\n");
        Require(status.Contains("\"line\":88", StringComparison.Ordinal), "Unity package next did not stop at line 88.");

        response = SendDebugCommand(debugPort, "{\"command\":\"continue\"}\n");
        Require(response.Contains("\"continued\":true", StringComparison.Ordinal), "Unity package continue command failed.");
        Require(script.Wait(TimeSpan.FromSeconds(5)), "Unity package debug runtime did not finish.");
    }

    private static Func<string, string, string> CreateDemoHostInvoker(string engineName)
    {
        return (method, payload) =>
        {
            if (method == "ariadnets.log")
            {
                return "null";
            }
            if (method == "ariadnets.async.begin")
            {
                return "{\"ok\":true,\"result\":{\"requestId\":1}}";
            }
            if (method == "actors.create")
            {
                return "{\"ok\":true,\"result\":{\"id\":1,\"name\":\"DemoPlayer\"}}";
            }
            if (method == "actors.setTransform" || method == "actors.setParent" || method == "actors.destroy")
            {
                return "{\"ok\":true,\"result\":null}";
            }

            Require(method == "demo.getPlayer", $"Unexpected demo host method: {method}");
            Require(
                payload == "{\"requestedBy\":\"TypeScript\"}",
                $"Unexpected demo host payload: {payload}");
            return $"{{\"name\":\"Ariadne\",\"engine\":\"{engineName}\"}}";
        };
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

    private static void RunDebugStepScenario(
        List<string> logs,
        int port,
        int breakpointLine,
        string stepCommand,
        int expectedLine,
        string failureMessage)
    {
        using var runtimeReady = new ManualResetEventSlim(false);
        using var startScript = new ManualResetEventSlim(false);
        logs.Clear();
        var script = Task.Run(() =>
        {
            using var runtime = new ScriptRuntime(
                logs.Add,
                debugEnabled: true,
                debugHost: "127.0.0.1",
                debugPort: (ushort)port);
            runtimeReady.Set();
            startScript.Wait();
            runtime.Evaluate(
                "function inner() {" +
                "globalThis.__ariadnets_debug_line('step.ts', 20, 0, 'inner', {}, 'at inner (step.ts:20:0)\\nat outer (step.ts:10:0)');" +
                "host.log('inner body');" +
                "}" +
                "function outer() {" +
                "globalThis.__ariadnets_debug_line('step.ts', 10, 0, 'outer', {}, 'at outer (step.ts:10:0)');" +
                "inner();" +
                "globalThis.__ariadnets_debug_line('step.ts', 11, 0, 'outer', {}, 'at outer (step.ts:11:0)');" +
                "host.log('outer after');" +
                "}" +
                "outer();",
                "step-fixture.js");
        });

        Require(runtimeReady.Wait(TimeSpan.FromSeconds(3)), "Step debug runtime did not start.");
        var response = SendDebugCommand(port, $"{{\"command\":\"setBreakpoint\",\"module\":\"step.ts\",\"line\":{breakpointLine}}}\n");
        Require(response.Contains("\"ok\":true", StringComparison.Ordinal), "Step breakpoint was not set.");

        startScript.Set();
        Thread.Sleep(200);
        Require(!script.IsCompleted, "Step scenario should pause at initial breakpoint.");

        var status = SendDebugCommand(port, "{\"command\":\"status\"}\n");
        Require(status.Contains($"\"line\":{breakpointLine}", StringComparison.Ordinal), "Step scenario initial line did not match.");

        response = SendDebugCommand(port, stepCommand);
        Require(response.Contains("\"continued\":true", StringComparison.Ordinal), "Step command response did not continue.");
        Thread.Sleep(200);
        Require(!script.IsCompleted, "Step scenario should pause after step command.");

        status = SendDebugCommand(port, "{\"command\":\"status\"}\n");
        Require(status.Contains($"\"line\":{expectedLine}", StringComparison.Ordinal), failureMessage);

        response = SendDebugCommand(port, "{\"command\":\"continue\"}\n");
        Require(response.Contains("\"continued\":true", StringComparison.Ordinal), "Step scenario continue response did not match.");
        Require(script.Wait(TimeSpan.FromSeconds(3)), "Step scenario did not finish after continue.");
        Require(logs.Contains("outer after"), "Step scenario did not finish script body.");
    }

    private static string SendDebugCommand(int port, string command)
    {
        using var client = new TcpClient();
        var connected = false;
        Exception lastConnectException = null;
        for (var attempt = 0; attempt < 20 && !connected; ++attempt)
        {
            try
            {
                client.Connect("127.0.0.1", port);
                connected = true;
            }
            catch (SocketException exception)
            {
                lastConnectException = exception;
                Thread.Sleep(50);
            }
        }
        if (!connected)
        {
            throw new InvalidOperationException("Could not connect to debug endpoint.", lastConnectException);
        }

        using var stream = client.GetStream();

        var buffer = new byte[512];
        var read = stream.Read(buffer, 0, buffer.Length);
        var greeting = Encoding.UTF8.GetString(buffer, 0, read);
        Require(
            greeting.Contains("AriadneTS debug endpoint connected", StringComparison.Ordinal),
            "Debug command endpoint greeting did not match.");

        var commandBytes = Encoding.UTF8.GetBytes(command);
        stream.Write(commandBytes, 0, commandBytes.Length);
        using var response = new MemoryStream();
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            response.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(response.ToArray());
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
