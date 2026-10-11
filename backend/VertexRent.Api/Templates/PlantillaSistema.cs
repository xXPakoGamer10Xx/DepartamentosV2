using System.Reflection;

namespace VertexRent.Api;

/// <summary>
/// "Contrato base": la plantilla por defecto del sistema es el Word propio de la empresa (con {{variables}}), incluido como
/// recurso embebido. El admin puede descargarlo, ajustarlo en Word y volver a subirlo como plantilla propia.
/// </summary>
public static class PlantillaSistema
{
    public const string Id = "sistema";
    public const string Nombre = "Contrato base";
    public const string Descripcion = "Contrato de arrendamiento estándar, listo para usar.";

    static readonly Lazy<byte[]> _docx = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("contrato_base.docx")
            ?? throw new InvalidOperationException("Falta el recurso contrato_base.docx");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    });
    public static byte[] Docx => _docx.Value;
}
