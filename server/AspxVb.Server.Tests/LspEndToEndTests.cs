using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using StreamJsonRpc;

namespace AspxVb.Server.Tests;

/// <summary>Starts the real server process and talks LSP to it over stdio, as VS Code does.</summary>
public class LspEndToEndTests
{
    [Fact]
    public async Task Opens_a_code_behind_publishes_diagnostics_and_completes()
    {
        var serverDll = Path.Combine(AppContext.BaseDirectory, "AspxVb.Server.dll");
        // Same arguments vscode-languageclient passes with the stdio transport.
        using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{serverDll}\" --stdio --clientProcessId={Environment.ProcessId}")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        })!;
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, formatter));
        var published = new ConcurrentDictionary<string, JsonElement>();
        var gotDiagnostics = new TaskCompletionSource();
        var onDiagnostics = new Action<JsonElement>(p =>
        {
            published[p.GetProperty("uri").GetString()!] = p.GetProperty("diagnostics").Clone();
            gotDiagnostics.TrySetResult();
        });
        rpc.AddLocalRpcMethod(onDiagnostics.Method, onDiagnostics.Target,
            new JsonRpcMethodAttribute("textDocument/publishDiagnostics") { UseSingleObjectParameterDeserialization = true });
        rpc.StartListening();

        try
        {
            var init = await rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize", new { processId = Environment.ProcessId, rootUri = (string?)null, capabilities = new { } });
            Assert.True(init.GetProperty("capabilities").GetProperty("definitionProvider").GetBoolean());
            await rpc.NotifyWithParameterObjectAsync("initialized", new { });

            var path = Sample.File("Default.aspx.vb");
            var uri = new Uri(path).AbsoluteUri;
            var text = File.ReadAllText(path);
            await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", new { textDocument = new { uri, languageId = "vb", version = 1, text } });

            await gotDiagnostics.Task.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(0, published[uri].GetArrayLength());

            var offset = Sample.At(text, "TextHelpers.Clean(txtName.");
            var line = text[..offset].Count(c => c == '\n');
            var character = offset - (text.LastIndexOf('\n', offset - 1) + 1);
            var list = await rpc.InvokeWithParameterObjectAsync<JsonElement>("textDocument/completion",
                new { textDocument = new { uri }, position = new { line, character } });
            Assert.Contains(list.GetProperty("items").EnumerateArray(), i => i.GetProperty("label").GetString() == "Text");

            await rpc.InvokeWithParameterObjectAsync<JsonElement?>("shutdown");
            await rpc.NotifyAsync("exit");
            Assert.True(process.WaitForExit(10_000));
        }
        finally
        {
            if (!process.HasExited) process.Kill();
        }
    }
}
