using System.IO.Compression;
using System.Text;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Export;

// P17: exporting the generator's own output (the .feature/.cs/.json files P5 produces), as
// opposed to ExcelTestCaseWriter/XmlTestCaseWriter which export the test-case *documentation*
// view (P6). One entry per GeneratedFile, named by its own RelativePath — unzipping
// reproduces the same folder layout GeneratedProjectWriter would have written, with every
// file already at its correct real extension.
public static class GeneratedFilesZipWriter
{
    // Encoding.UTF8's static instance emits a byte-order-mark preamble; File.WriteAllText
    // (what GeneratedProjectWriter actually writes to disk with) does not. An unzipped file
    // must be byte-identical to what generation would have written to the real project —
    // otherwise unzipping and diffing against a fresh Generate produces a spurious one-byte
    // diff on every single file, on line 1, forever.
    private static readonly UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static byte[] WriteBytes(IEnumerable<GeneratedFile> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                // ZipArchive entry names always use '/', regardless of the host OS —
                // RelativePath already does, but normalize defensively since it travels
                // through JSON from the frontend.
                var entryName = file.RelativePath.Replace('\\', '/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, NoBom);
                writer.Write(file.Content);
            }
        }

        return stream.ToArray();
    }
}
