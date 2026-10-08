using System.IO;
using System.Globalization;
using System.Text;
using HeThongQuanLyVeXeBus.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QRCoder;

namespace HeThongQuanLyVeXeBus.Services;

/// <summary>Xuất vé A4 để in hoặc gửi cho khách; dữ liệu QR chỉ nằm trong tệp PDF.</summary>
public static class TicketPdfExporter
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly XBrush Ink = new XSolidBrush(XColor.FromArgb(29, 43, 59));
    private static readonly XBrush Muted = new XSolidBrush(XColor.FromArgb(88, 104, 117));
    private static readonly XBrush Accent = new XSolidBrush(XColor.FromArgb(0, 111, 128));
    private static readonly XBrush Pale = new XSolidBrush(XColor.FromArgb(237, 247, 249));
    private static readonly XBrush White = XBrushes.White;

    public static void Export(Ticket ticket, string path)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Vui lòng chọn tệp PDF đích.", nameof(path));
        if (ticket.Status == TicketStatus.Cancelled)
            throw new InvalidOperationException("Không thể xuất vé đã hủy thành vé đi xe.");
        if (ticket.Status is not (TicketStatus.Paid or TicketStatus.CheckedIn))
            throw new InvalidOperationException("Trạng thái vé không hợp lệ để xuất PDF.");
        if (ticket.Trip?.Route is null || ticket.Trip.Bus is null)
            throw new InvalidOperationException("Cần tải chuyến xe, tuyến đường và xe trước khi xuất vé.");
        if (string.IsNullOrWhiteSpace(ticket.TicketCode))
            throw new InvalidOperationException("Vé chưa có mã để nhận diện và soát vé.");

        var qrContent = string.IsNullOrWhiteSpace(ticket.QrData) ? ticket.TicketCode : ticket.QrData;
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Thư mục xuất PDF không tồn tại: {directory}");
        if (Directory.Exists(destination))
            throw new ArgumentException("Đường dẫn xuất PDF phải là tên tệp, không phải thư mục.", nameof(path));

        // Build the PDF in memory first. The selected file is touched only after successful generation.
        using var document = new PdfDocument();
        document.Info.Title = $"Vé xe {ticket.TicketCode}";
        document.Info.Subject = "Vé xe khách và mã QR soát vé";
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            // PDFsharp-WPF resolves Segoe UI through WPF and embeds its Unicode glyphs in the PDF.
            var regular = new XFont("Segoe UI", 11, XFontStyleEx.Regular);
            var bold = new XFont("Segoe UI", 11, XFontStyleEx.Bold);
            var small = new XFont("Segoe UI", 9, XFontStyleEx.Regular);
            var heading = new XFont("Segoe UI", 19, XFontStyleEx.Bold);
            var prominent = new XFont("Segoe UI", 15, XFontStyleEx.Bold);
            var priceFont = new XFont("Segoe UI", 16, XFontStyleEx.Bold);
            var width = page.Width.Point;

            gfx.DrawRectangle(Accent, 0, 0, width, 11);
            gfx.DrawString("VÉ XE KHÁCH", heading, Ink, new XRect(40, 33, 300, 30), XStringFormats.TopLeft);
            gfx.DrawString("THÔNG TIN HÀNH TRÌNH", small, Muted, new XRect(40, 69, 300, 20), XStringFormats.TopLeft);
            gfx.DrawRectangle(Pale, 398, 36, 157, 39);
            DrawLines(gfx, ticket.Status == TicketStatus.CheckedIn ? "ĐÃ SOÁT VÉ" : "VÉ ĐÃ THANH TOÁN",
                bold, Accent, 406, 47, 140, 1, 17);
            Label(gfx, "MÃ VÉ", 40, 97, small);
            DrawLines(gfx, ticket.TicketCode, bold, Ink, 40, 113, width - 80, 2, 16);
            Divider(gfx, 155, width);

            Label(gfx, "TUYẾN ĐƯỜNG", 40, 169, small);
            DrawLines(gfx, ticket.Trip.Route.RouteName, prominent, Ink, 40, 187, width - 80, 2, 21);
            Field(gfx, "ĐIỂM ĐI", ticket.Trip.Route.Origin, 40, 247, 246, small, bold);
            Field(gfx, "ĐIỂM ĐẾN", ticket.Trip.Route.Destination, 310, 247, 245, small, bold);
            Divider(gfx, 313, width);

            Field(gfx, "KHỞI HÀNH", ticket.Trip.DepartureTime.ToString("HH:mm · dd/MM/yyyy", Vietnamese),
                40, 327, 246, small, bold);
            Field(gfx, "DỰ KIẾN ĐẾN", ticket.Trip.EstimatedArrivalTime.ToString("HH:mm · dd/MM/yyyy", Vietnamese),
                310, 327, 245, small, bold);
            Field(gfx, "ĐIỂM ĐÓN", ticket.PickupPoint, 40, 402, 246, small, regular);
            Field(gfx, "ĐIỂM TRẢ", ticket.DropoffPoint, 310, 402, 245, small, regular);
            Divider(gfx, 479, width);

            Field(gfx, "HÀNH KHÁCH", ticket.CustomerName, 40, 493, 246, small, bold);
            Field(gfx, "MÃ CHUYẾN", ticket.Trip.TripCode, 310, 493, 245, small, bold);
            Field(gfx, "SỐ ĐIỆN THOẠI", ticket.CustomerPhone, 40, 560, 246, small, regular);
            Field(gfx, "BIỂN SỐ XE", ticket.Trip.Bus.PlateNumber, 310, 560, 245, small, regular);
            Field(gfx, "HÌNH THỨC THANH TOÁN", ticket.PaymentMethod, 40, 627, 246, small, regular);
            Label(gfx, "SỐ TIỀN VÉ", 310, 627, small);
            DrawLines(gfx, ticket.Price.ToString("N0", Vietnamese) + " ₫", priceFont, Accent,
                310, 647, 245, 1, 22);

            Divider(gfx, 691, width);
            Label(gfx, ticket.Status == TicketStatus.CheckedIn ? "TRẠNG THÁI: ĐÃ SOÁT VÉ" : "TRẠNG THÁI: ĐÃ THANH TOÁN",
                40, 708, bold);
            DrawLines(gfx, ticket.Status == TicketStatus.CheckedIn
                    ? $"Đã soát vé: {(ticket.CheckedInAt.HasValue ? ticket.CheckedInAt.Value.ToString("HH:mm · dd/MM/yyyy", Vietnamese) : "Không có thời gian ghi nhận")}" 
                    : $"Đặt vé: {ticket.BookedAt.ToString("HH:mm · dd/MM/yyyy", Vietnamese)}",
                small, Muted, 40, 730, 365, 2, 15);
            DrawLines(gfx, ticket.Status == TicketStatus.CheckedIn
                    ? "Mã QR dùng để đối chiếu vé đã soát." : "Xuất trình mã QR khi lên xe để soát vé.",
                small, Muted, 40, 770, 365, 2, 15);

            using var generator = new QRCodeGenerator();
            using var qrData = generator.CreateQrCode(qrContent, QRCodeGenerator.ECCLevel.Q);
            using var qr = new PngByteQRCode(qrData);
            var qrPng = qr.GetGraphic(8);
            using var stream = new MemoryStream(qrPng, 0, qrPng.Length, false, true);
            using var image = XImage.FromStream(stream);
            gfx.DrawImage(image, 436, 705, 105, 105);
        }

        // Same-directory staging allows replacement only after Save has completed successfully.
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.pdf");
        try
        {
            document.Save(temporary);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void Field(XGraphics gfx, string label, string? value, double x, double y, double width,
        XFont labelFont, XFont valueFont)
    {
        Label(gfx, label, x, y, labelFont);
        DrawLines(gfx, value, valueFont, Ink, x, y + 19, width, 2, 17);
    }

    private static void Label(XGraphics gfx, string text, double x, double y, XFont font) =>
        gfx.DrawString(text, font, Muted, new XRect(x, y, 380, 18), XStringFormats.TopLeft);

    private static void Divider(XGraphics gfx, double y, double pageWidth) =>
        gfx.DrawLine(new XPen(XColor.FromArgb(221, 229, 233), 0.8), 40, y, pageWidth - 40, y);

    // PDFsharp does not wrap DrawString text. Fit each text-element (not UTF-16 code units),
    // and ellipsize the last visible line so long place names never overlap adjacent fields.
    private static void DrawLines(XGraphics gfx, string? value, XFont font, XBrush brush,
        double x, double y, double width, int maxLines, double lineHeight)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "Chưa cập nhật" :
            string.Join(' ', value.Normalize(NormalizationForm.FormC)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var elements = StringInfo.GetTextElementEnumerator(text);
        var lines = new List<string>(maxLines);
        var current = new StringBuilder();
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var candidate = current.ToString() + element;
            if (gfx.MeasureString(candidate, font).Width <= width || current.Length == 0)
            {
                current.Append(element);
                continue;
            }

            lines.Add(current.ToString().TrimEnd());
            current.Clear();
            if (lines.Count == maxLines)
            {
                var shortened = lines[^1];
                var glyphs = StringInfo.ParseCombiningCharacters(shortened);
                while (glyphs.Length > 0 && gfx.MeasureString(shortened + "…", font).Width > width)
                {
                    shortened = shortened[..glyphs[^1]];
                    glyphs = StringInfo.ParseCombiningCharacters(shortened);
                }
                lines[^1] = shortened.TrimEnd() + "…";
                break;
            }
            if (!string.IsNullOrWhiteSpace(element) || lines[^1].Length == 0)
                current.Append(element);
        }
        if (lines.Count < maxLines && current.Length > 0)
            lines.Add(current.ToString().TrimEnd());
        for (var index = 0; index < lines.Count; index++)
            gfx.DrawString(lines[index], font, brush,
                new XRect(x, y + index * lineHeight, width, lineHeight), XStringFormats.TopLeft);
    }
}
