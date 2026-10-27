using Udpi30.Ontology.Models;

namespace Udpi30.Ontology.Tests;

public class OntologyIoTests
{
    [Fact]
    public void SaveAndLoad_EditableMetadata_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{OntologyIo.FileExtension}");
        var document = new OntologyDocument
        {
            Name = "CustomerOntology",
            Description = "Customer domain",
            SourceModelName = "Customer Model",
            Entities =
            {
                new OntologyEntity
                {
                    Id = "101",
                    Name = "Customer",
                    LogicalName = "Customer Account",
                    Description = "A customer account.",
                },
            },
        };

        try
        {
            OntologyIo.Save(document, path);
            var loaded = OntologyIo.Load(path);

            Assert.Equal(document.Name, loaded.Name);
            Assert.Equal(document.Description, loaded.Description);
            Assert.Equal("Customer Account", Assert.Single(loaded.Entities).LogicalName);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
