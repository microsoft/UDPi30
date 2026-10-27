// Entry point for the `udpi30` CLI — Project definition for Microsoft Unified Data Platform.
using Udpi30.Cli;

return CliApp.BuildRootCommand().Parse(args).Invoke();
