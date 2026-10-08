using System.IO;
using ClosedXML.Excel;
using HeThongQuanLyVeXeBus.Models;

namespace HeThongQuanLyVeXeBus.Services;

public static class TicketExcelExporter
{
    private static readonly string[] Headers =
    [
        "Mã vé", "Mã chuyến", "Tuyến", "Khởi hành",
        "Khách hàng", "Số điện thoại", "Email", "Điểm đón", "Điểm trả",
        "Giá vé (VND)", "Thanh toán", "Trạng thái", "Thời gian soát vé",
        "Trạng thái hoàn tiền", "Thời gian hoàn tiền"
    ];

    private static readonly double[] ColumnWidths =
    [
        22, 22, 30, 20, 25, 19, 30, 28, 28, 19, 27, 28, 22, 23, 22
    ];

    public static void Export(IReadOnlyCollection<Ticket> tickets, string path)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Thư mục lưu báo cáo không tồn tại: {directory}");

        using var workbook = new XLWorkbook();
        var checkedIn = new List<Ticket>();
        WriteSheet(workbook.Worksheets.Add("Tất cả vé"), tickets, checkedIn);
        WriteSheet(workbook.Worksheets.Add("Đã soát vé"), checkedIn, null);

        // Build the entire workbook before touching the chosen destination. A failed save
        // leaves only a temporary file, rather than truncating an existing report.
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.xlsx");
        try
        {
            workbook.SaveAs(temporaryPath);
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void WriteSheet(IXLWorksheet sheet, IEnumerable<Ticket> tickets, List<Ticket>? checkedIn)
    {
        for (int column = 1; column <= Headers.Length; column++)
        {
            SetText(sheet.Cell(1, column), Headers[column - 1]);
            sheet.Column(column).Width = ColumnWidths[column - 1];
        }

        var header = sheet.Range(1, 1, 1, Headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#174A76");
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Row(1).Height = 28;
        sheet.SheetView.FreezeRows(1);

        int row = 2;
        decimal validTotal = 0;
        foreach (Ticket ticket in tickets)
        {
            if (ticket is null)
                throw new ArgumentException("Danh sách vé chứa một vé null.", nameof(tickets));

            WriteTicket(sheet, row++, ticket);
            if (ticket.Status != TicketStatus.Cancelled)
                validTotal += ticket.Price;
            if (ticket.Status == TicketStatus.CheckedIn)
                checkedIn?.Add(ticket);
        }

        // A header-only filter is valid for an empty export; it does not invent a data row.
        sheet.Range(1, 1, row - 1, Headers.Length).SetAutoFilter();
        sheet.Column(10).Style.NumberFormat.Format = "#,##0";
        sheet.Column(4).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        sheet.Column(13).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        sheet.Column(15).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

        int totalRow = row + 1;
        sheet.Range(totalRow, 1, totalRow, 9).Merge();
        SetText(sheet.Cell(totalRow, 1), "Tổng giá vé chưa hủy (không phải số liệu đối soát ngân hàng)");
        sheet.Cell(totalRow, 10).SetValue(validTotal);
        sheet.Range(totalRow, 1, totalRow, 10).Style.Font.Bold = true;
    }

    private static void WriteTicket(IXLWorksheet sheet, int row, Ticket ticket)
    {
        Trip trip = ticket.Trip ?? throw new InvalidOperationException($"Vé {ticket.TicketCode} chưa tải thông tin chuyến xe.");
        BusRoute route = trip.Route ?? throw new InvalidOperationException($"Vé {ticket.TicketCode} chưa tải thông tin tuyến xe.");

        SetText(sheet.Cell(row, 1), ticket.TicketCode);
        SetText(sheet.Cell(row, 2), trip.TripCode);
        SetText(sheet.Cell(row, 3), route.RouteName);
        sheet.Cell(row, 4).SetValue(trip.DepartureTime);
        SetText(sheet.Cell(row, 5), ticket.CustomerName);
        SetText(sheet.Cell(row, 6), ticket.CustomerPhone);
        SetText(sheet.Cell(row, 7), ticket.CustomerEmail);
        SetText(sheet.Cell(row, 8), ticket.PickupPoint);
        SetText(sheet.Cell(row, 9), ticket.DropoffPoint);
        sheet.Cell(row, 10).SetValue(ticket.Price);
        SetText(sheet.Cell(row, 11), ticket.PaymentMethod);
        SetText(sheet.Cell(row, 12), ticket.StatusText);
        if (ticket.CheckedInAt is { } checkedInAt)
            sheet.Cell(row, 13).SetValue(checkedInAt);
        SetText(sheet.Cell(row, 14), ticket.Status == TicketStatus.Cancelled
            ? ticket.RefundedAt.HasValue ? "Đã hoàn tiền" : "Chưa hoàn tiền"
            : "Không áp dụng");
        if (ticket.Status == TicketStatus.Cancelled && ticket.RefundedAt is { } refundedAt)
            sheet.Cell(row, 15).SetValue(refundedAt);
    }

    private static void SetText(IXLCell cell, string? text)
    {
        // Explicit XLSX string cells never become executable formulas, including values
        // starting with =, +, - or @. Text formatting also preserves phone leading zeros.
        cell.Style.NumberFormat.Format = "@";
        cell.SetValue(text ?? string.Empty);
    }
}
