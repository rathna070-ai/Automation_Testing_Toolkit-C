using System.IO.Compression;
using System.Text;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Export.Tests;

public class GeneratedFilesZipWriterTests
{
    private static readonly List<GeneratedFile> SampleFiles =
    [
        new GeneratedFile("Features/Login.feature", "Feature: Login\n  Scenario: ..."),
        new GeneratedFile("Steps/LoginSteps.cs", "public class LoginSteps { }"),
        new GeneratedFile("PageObjects/LoginPage.cs", "public class LoginPage { }"),
        new GeneratedFile("LocatorRepository/LoginPage.locators.json", "{ \"url\": \"https://example.com\" }")
    ];

    [Test]
    public void WriteBytes_EntryNamesMatchRelativePathExactly_IncludingNestedFolders()
    {
        var bytes = GeneratedFilesZipWriter.WriteBytes(SampleFiles);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entryNames = archive.Entries.Select(e => e.FullName).ToList();

        Assert.That(entryNames, Is.EquivalentTo(SampleFiles.Select(f => f.RelativePath)));
    }

    [Test]
    public void WriteBytes_EntryContentIsByteIdenticalToTheSourceFile()
    {
        var bytes = GeneratedFilesZipWriter.WriteBytes(SampleFiles);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        foreach (var file in SampleFiles)
        {
            var entry = archive.GetEntry(file.RelativePath);
            Assert.That(entry, Is.Not.Null, $"Missing entry for {file.RelativePath}");

            // Read raw bytes rather than through a StreamReader: StreamReader auto-detects
            // and strips a BOM on the way in, which would silently hide one going out —
            // exactly the bug this test exists to catch (see the next test).
            using var entryStream = entry!.Open();
            using var raw = new MemoryStream();
            entryStream.CopyTo(raw);

            Assert.That(raw.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(file.Content)),
                "An unzipped file must be byte-identical to what GeneratedProjectWriter would " +
                "have written to disk, including having no BOM.");
        }
    }

    // File.WriteAllText (what GeneratedProjectWriter actually writes with) never emits a
    // UTF-8 byte-order-mark. If the zip did, every single unzipped file would carry a
    // spurious 3-byte prefix invisible in most editors but present in any byte-for-byte diff
    // against a fresh Generate — caught live once already; pinned here so it can't recur.
    [Test]
    public void WriteBytes_NeverEmitsAUtf8Bom()
    {
        var bytes = GeneratedFilesZipWriter.WriteBytes(SampleFiles);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            var head = new byte[3];
            var read = entryStream.Read(head, 0, 3);

            Assert.That(read < 3 || !head.SequenceEqual(bom), Is.True,
                $"{entry.FullName} starts with a UTF-8 BOM.");
        }
    }

    [Test]
    public void WriteBytes_BackslashPathsAreNormalizedToForwardSlashEntries()
    {
        var files = new List<GeneratedFile> { new(@"PageObjects\LoginPage.cs", "content") };
        var bytes = GeneratedFilesZipWriter.WriteBytes(files);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

        Assert.That(archive.Entries.Single().FullName, Is.EqualTo("PageObjects/LoginPage.cs"));
    }

    [Test]
    public void WriteBytes_EmptyFileList_ProducesAValidEmptyArchive()
    {
        var bytes = GeneratedFilesZipWriter.WriteBytes([]);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.That(archive.Entries, Is.Empty);
    }
}
