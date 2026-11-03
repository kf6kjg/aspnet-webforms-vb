using AspxVb.Server;
using AspxVb.Server.Lsp;

// --check <folder>: compile every site under the folder and summarize diagnostics.
// Anything else: a language server on stdin/stdout. vscode-languageclient adds flags such
// as --stdio and --clientProcessId=N; they need no handling, so they're ignored.
if (args.Length > 0 && args[0] == "--check")
{
    if (args.Length == 2) return await Check.RunAsync(args[1]);
    Console.Error.WriteLine("usage: AspxVb.Server --check <folder>");
    return 2;
}
await LspServer.RunAsync();
return 0;
