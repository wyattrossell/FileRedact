using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using FileRedact.Core.Pdf;

namespace FileRedact.Core.Documents;

/// <summary>
/// Normalises any supported input to a PDF. Word formats are converted with Microsoft Word when it is
/// installed (best fidelity, uses Word's own PDF export); otherwise a built-in text-only conversion is
/// used for .docx and .txt. Images are wrapped in a single-page PDF and later OCR'd.
/// </summary>
public static class DocumentConverter
{
    public static readonly string[] WordExtensions = { ".docx", ".docm", ".doc", ".dotx", ".dot", ".rtf", ".odt", ".txt" };
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    public static string FileDialogFilter =>
        "All supported|*.pdf;*.docx;*.docm;*.doc;*.rtf;*.odt;*.txt;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|" +
        "PDF documents|*.pdf|Word documents|*.docx;*.docm;*.doc;*.dotx;*.dot;*.rtf;*.odt|Text files|*.txt|Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*";

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".pdf" || WordExtensions.Contains(ext) || ImageExtensions.Contains(ext);
    }

    /// <summary>Returns the path of a PDF representing <paramref name="inputPath"/> and a note describing how it was produced.</summary>
    public static async Task<(string PdfPath, string Note)> ToPdfAsync(string inputPath, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        if (ext == ".pdf") return (inputPath, "PDF");

        var output = AppPaths.NewWorkFile(".pdf");

        if (WordExtensions.Contains(ext))
        {
            var viaWord = await Task.Run(() => TryConvertWithWord(inputPath, output), ct);
            if (viaWord) return (output, "Converted with Microsoft Word");

            if (ext == ".docx" || ext == ".docm")
            {
                SimplePdfBuilder.FromDocx(inputPath, output);
                return (output, "Converted with built-in text layout (Microsoft Word not available - formatting simplified)");
            }
            if (ext == ".txt")
            {
                SimplePdfBuilder.FromText(File.ReadAllText(inputPath), output);
                return (output, "Converted plain text");
            }
            throw new NotSupportedException($"Converting {ext} files requires Microsoft Word to be installed. Alternatively print the document to the FileRedact printer.");
        }

        if (ImageExtensions.Contains(ext))
        {
            SimplePdfBuilder.FromImage(inputPath, output);
            return (output, "Image wrapped in PDF; text recovered with OCR");
        }

        throw new NotSupportedException($"Unsupported file type: {ext}");
    }

    /// <summary>Uses Word's COM automation via late binding, so no interop assembly or specific Office version is required.</summary>
    private static bool TryConvertWithWord(string input, string output)
    {
        var type = Type.GetTypeFromProgID("Word.Application");
        if (type == null) return false;

        object? app = null, docs = null, doc = null;
        try
        {
            app = Activator.CreateInstance(type);
            if (app == null) return false;
            Com.Set(app, "Visible", false);
            Com.Set(app, "DisplayAlerts", 0); // wdAlertsNone
            docs = Com.Get(app, "Documents");
            // Documents.Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles)
            doc = Com.Call(docs!, "Open", Path.GetFullPath(input), false, true, false);
            // Document.ExportAsFixedFormat(OutputFileName, ExportFormat=wdExportFormatPDF(17))
            Com.Call(doc!, "ExportAsFixedFormat", Path.GetFullPath(output), 17);
            return File.Exists(output) && new FileInfo(output).Length > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Word conversion failed: {ex}");
            return false;
        }
        finally
        {
            try { if (doc != null) Com.Call(doc, "Close", 0); } catch { }
            try { if (app != null) Com.Call(app, "Quit", 0); } catch { }
            foreach (var o in new[] { doc, docs, app })
                if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o);
        }
    }

    private static class Com
    {
        public static object? Get(object o, string name) => o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
        public static void Set(object o, string name, object value) => o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new[] { value });
        public static object? Call(object o, string name, params object[] args) => o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);
    }
}
