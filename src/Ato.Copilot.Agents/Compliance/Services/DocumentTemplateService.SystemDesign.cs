using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Agents.Compliance.Services;

public partial class DocumentTemplateService
{
    private async Task<SystemDesignDocumentData?> LoadDesignAsync(string systemId, string documentType, CancellationToken ct)
    {
        if (documentType != "ssp") return null;
        using var scope = _scopeFactory.CreateScope();
        return await SystemDesignDocumentData.LoadAsync(scope.ServiceProvider, systemId, ct);
    }

    private static byte[] AppendDesignDiagrams(byte[] document, SystemDesignDocumentData design, bool includeText)
    {
        using var output = new MemoryStream();
        output.Write(document);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            XNamespace r = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
            var bodyDocument = ReadXml(zip, "word/document.xml");
            var body = bodyDocument.Root?.Element(w + "body") ?? throw new InvalidDataException("Word document body is unavailable.");
            var relationships = zip.GetEntry("word/_rels/document.xml.rels") == null
                ? new XDocument(new XElement(r + "Relationships"))
                : ReadXml(zip, "word/_rels/document.xml.rels");
            var contentTypes = ReadXml(zip, "[Content_Types].xml");
            if (!contentTypes.Root!.Elements().Any(x => x.Attribute("Extension")?.Value == "svg"))
                contentTypes.Root.Add(new XElement(types + "Default", new XAttribute("Extension", "svg"), new XAttribute("ContentType", "image/svg+xml")));
            void Paragraph(string text) => body.Add(new XElement(w + "p", new XElement(w + "r",
                new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text))));
            if (includeText)
                foreach (var line in design.Text.Split('\n')) Paragraph(line);
            var drawingId = bodyDocument.Descendants().Where(x => x.Name.LocalName == "docPr")
                .Select(x => uint.TryParse(x.Attribute("id")?.Value, out var id) ? id : 0).DefaultIfEmpty().Max() + 1;
            foreach (var artifact in design.Artifacts)
            {
                var relId = $"design-{artifact.Id}";
                var mediaName = $"word/media/{artifact.FileName}";
                zip.GetEntry(mediaName)?.Delete();
                using (var stream = zip.CreateEntry(mediaName).Open()) stream.Write(artifact.Content);
                relationships.Root!.Add(new XElement(r + "Relationship", new XAttribute("Id", relId),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                    new XAttribute("Target", $"media/{artifact.FileName}")));
                Paragraph(artifact.Title);
                Paragraph(artifact.Description);
                var svg = XElement.Parse(Encoding.UTF8.GetString(artifact.Content));
                var width = (double)svg.Attribute("width")!;
                var height = (double)svg.Attribute("height")!;
                var scale = Math.Min(5_943_600 / width, 6_400_800 / height);
                var cx = (long)Math.Round(width * scale);
                var cy = (long)Math.Round(height * scale);
                var drawing = XElement.Parse($$"""
                    <w:p xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                      xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                      xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
                      <w:r><w:drawing><wp:inline>
                        <wp:extent cx="{{cx}}" cy="{{cy}}"/>
                        <wp:docPr id="{{drawingId++}}" name="System design diagram"/>
                        <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                          <pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="System design"/><pic:cNvPicPr/></pic:nvPicPr>
                            <pic:blipFill><a:blip r:embed="{{relId}}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                            <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{{cx}}" cy="{{cy}}"/></a:xfrm>
                              <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                            </pic:spPr>
                          </pic:pic>
                        </a:graphicData></a:graphic>
                      </wp:inline></w:drawing></w:r>
                    </w:p>
                    """);
                drawing.Descendants().Single(x => x.Name.LocalName == "docPr").SetAttributeValue("descr", artifact.Description);
                body.Add(drawing);
                Paragraph($"Diagram SHA-256: {artifact.ContentHash}");
            }
            var sectionProperties = body.Element(w + "sectPr");
            if (sectionProperties != null) { sectionProperties.Remove(); body.Add(sectionProperties); }
            WriteXml(zip, "word/document.xml", bodyDocument);
            WriteXml(zip, "word/_rels/document.xml.rels", relationships);
            WriteXml(zip, "[Content_Types].xml", contentTypes);
        }
        return output.ToArray();
    }

    private static XDocument ReadXml(ZipArchive zip, string path)
    {
        using var stream = (zip.GetEntry(path) ?? throw new InvalidDataException($"Document part {path} is unavailable.")).Open();
        return XDocument.Load(stream);
    }

    private static void WriteXml(ZipArchive zip, string path, XDocument document)
    {
        zip.GetEntry(path)?.Delete();
        using var stream = zip.CreateEntry(path).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }
}
