namespace Udpi30.Editor;

/// <summary>Entry point for the standalone udp.yml WinForms editor.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Register PropertyGrid type editors/converters for the Udpi30.Core
        // model graph before any grid is shown.
        EditorRegistry.RegisterAll();

        var form = new MainForm();

        // Optional file argument: udp.yml path to open on launch.
        var path = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            form.OpenOnLoad(path);
        }

        Application.Run(form);
    }
}
