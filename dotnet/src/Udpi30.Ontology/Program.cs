namespace Udpi30.Ontology;

/// <summary>
/// Entry point for the UDPI30 Ontology Builder — a WinForms tool that reads a
/// Microsoft Fabric semantic model, turns it into a digital-twin-builder ontology
/// (entity types, properties, relationship types) with auto-generated logical
/// names and descriptions, saves it to a local <c>.ontology.json</c> file, and
/// publishes it as an Ontology item into a Fabric workspace.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var form = new MainForm();

        // Optional file argument: an .ontology.json path to open on launch.
        var path = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            form.OpenOnLoad(path);
        }

        Application.Run(form);
    }
}
