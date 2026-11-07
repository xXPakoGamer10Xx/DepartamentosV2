using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VertexRent.Api;

/// <summary>Contrato a partir de párrafos HTML parseados (plantilla del admin o DOCX).</summary>
public static class ContratoPersonalizadoTemplate
{
    public static byte[] Render(List<ParsedPara> paragraphs, string nombreCompleto, string arrendadorNombre)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginTop(44);
                page.MarginBottom(26);
                page.MarginHorizontal(55);
                page.DefaultTextStyle(x => x.FontFamily(PdfService.FontFamilies).FontSize(10).LineHeight(1.6f).FontColor("#000000"));

                page.Content().Column(col =>
                {
                    foreach (var para in paragraphs)
                    {
                        var (top, bottom) = para.Level switch { "h1" => (0f, 10f), "h2" => (10f, 5f), "h3" => (8f, 4f), _ => (0f, 8f) };
                        col.Item().PaddingTop(top).PaddingBottom(bottom).Text(t =>
                        {
                            switch (para.Level)
                            {
                                case "h1": t.AlignCenter(); t.DefaultTextStyle(s => s.FontSize(14).Bold().LetterSpacing(0.07f)); break;
                                case "h2": t.DefaultTextStyle(s => s.FontSize(11).Bold().Underline()); break;
                                case "h3": t.DefaultTextStyle(s => s.Bold()); break;
                                default: t.Justify(); break;
                            }
                            foreach (var seg in para.Segments)
                            {
                                var text = para.Level == "h1" ? seg.Text.ToUpperInvariant() : seg.Text;
                                var span = t.Span(text);
                                if (seg.Bold) span.Bold();
                                else if (seg.Italic) span.Italic();
                            }
                        });
                    }

                    // Firmas
                    col.Item().PaddingTop(50).ShowEntire().Row(row =>
                    {
                        row.RelativeItem(1);
                        row.RelativeItem(38).Column(c =>
                        {
                            c.Item().PaddingTop(30).LineHorizontal(1).LineColor("#000000");
                            c.Item().PaddingTop(5).AlignCenter().Text(t =>
                            {
                                t.DefaultTextStyle(s => s.FontSize(9).FontColor("#333333"));
                                t.Span("ARRENDADOR\n"); t.Span(arrendadorNombre);
                            });
                        });
                        row.RelativeItem(2);
                        row.RelativeItem(38).Column(c =>
                        {
                            c.Item().PaddingTop(30).LineHorizontal(1).LineColor("#000000");
                            c.Item().PaddingTop(5).AlignCenter().Text(t =>
                            {
                                t.DefaultTextStyle(s => s.FontSize(9).FontColor("#333333"));
                                t.Span("ARRENDATARIO\n"); t.Span(nombreCompleto);
                            });
                        });
                        row.RelativeItem(1);
                    });
                });

                page.Footer().PaddingTop(14).AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor("#777777"));
                    t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
