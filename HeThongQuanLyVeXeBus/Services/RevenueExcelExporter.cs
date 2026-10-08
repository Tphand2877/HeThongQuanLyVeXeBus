using System.IO;
using ClosedXML.Excel;

namespace HeThongQuanLyVeXeBus.Services;

public static class RevenueExcelExporter
{
    public static void Export(RevenueReport report, string routeName, string path)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(routeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Thư mục lưu báo cáo không tồn tại: {directory}");

        using var workbook = new XLWorkbook();
        WriteSummary(workbook.Worksheets.Add("Tổng hợp"), report, routeName);
        WriteTrend(workbook.Worksheets.Add("Xu hướng"), report.Trend);
        WriteRoutes(workbook.Worksheets.Add("Theo tuyến"), report.Routes);
        WritePayments(workbook.Worksheets.Add("Thanh toán"), report.Payments);
        WriteTrips(workbook.Worksheets.Add("Theo chuyến"), report.Trips);
        WriteTickets(workbook.Worksheets.Add("Danh sách vé"), report.Tickets);

        // Keep the existing destination intact if the workbook cannot be saved.
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

    private static void WriteSummary(IXLWorksheet sheet, RevenueReport report, string routeName)
    {
        sheet.Column(1).Width = 48;
        sheet.Column(2).Width = 32;
        sheet.Range(1, 1, 1, 2).Merge();
        SetText(sheet.Cell(1, 1), "BÁO CÁO DOANH THU VÉ");
        sheet.Range(1, 1, 1, 2).Style.Font.Bold = true;
        sheet.Row(1).Height = 28;

        SetText(sheet.Cell(2, 1), "Từ ngày bán vé");
        sheet.Cell(2, 2).SetValue(report.From);
        SetText(sheet.Cell(3, 1), "Đến ngày bán vé");
        sheet.Cell(3, 2).SetValue(report.To);
        sheet.Range(2, 2, 3, 2).Style.DateFormat.Format = "dd/MM/yyyy";
        SetText(sheet.Cell(4, 1), "Tuyến xe");
        SetText(sheet.Cell(4, 2), routeName);
        SetText(sheet.Cell(5, 1), "Nhóm xu hướng");
        SetText(sheet.Cell(5, 2), report.TrendGranularity);
        sheet.Range(7, 1, 7, 2).Merge();
        SetText(sheet.Cell(7, 1), "Chỉ tính vé chưa hủy; trạng thái hoàn tiền ghi nhận thủ công, không phải đối soát ngân hàng.");
        sheet.Row(7).Height = 30;
        sheet.Cell(7, 1).Style.Alignment.WrapText = true;

        Header(sheet, ["Chỉ tiêu", "Giá trị"], [48, 32], 9);
        string[] labels =
        [
            "Doanh thu vé chưa hủy (VND)", "Vé hiệu lực", "Đã soát vé", "Tổng vé đã bán",
            "Vé đã hủy", "Giá trị vé hủy (VND)", "Đã đánh dấu hoàn", "Chờ đánh dấu hoàn",
            "Giá trị đã đánh dấu hoàn (VND)", "Giá trị chờ đánh dấu hoàn (VND)"
        ];
        decimal[] values =
        [
            report.Revenue, report.ActiveCount, report.CheckedInCount, report.TicketCount,
            report.CancelledCount, report.CancelledValue, report.RefundedCount, report.PendingRefundCount,
            report.RefundedValue, report.PendingRefundValue
        ];
        for (int i = 0; i < labels.Length; i++)
        {
            SetText(sheet.Cell(i + 10, 1), labels[i]);
            sheet.Cell(i + 10, 2).SetValue(values[i]);
        }
        sheet.Range(10, 2, 19, 2).Style.NumberFormat.Format = "#,##0";
        sheet.Range(10, 1, 10, 2).Style.Font.Bold = true;
    }

    private static void WriteTrend(IXLWorksheet sheet, IReadOnlyList<RevenueTrend> rows)
    {
        Header(sheet, ["Kỳ bán vé", "Doanh thu (VND)", "Vé hiệu lực", "Vé đã hủy"], [26, 23, 18, 17]);
        int row = 2;
        foreach (var item in rows)
        {
            SetText(sheet.Cell(row, 1), item.Label);
            sheet.Cell(row, 2).SetValue(item.Revenue);
            sheet.Cell(row, 3).SetValue(item.ActiveTickets);
            sheet.Cell(row++, 4).SetValue(item.CancelledTickets);
        }
        FinishTable(sheet, row, 4, 2);
    }

    private static void WriteRoutes(IXLWorksheet sheet, IReadOnlyList<RevenueRoute> rows)
    {
        Header(sheet, ["Mã tuyến", "Tuyến", "Vé hiệu lực", "Đã soát", "Đã hủy", "Doanh thu (VND)"],
            [18, 32, 18, 16, 16, 23]);
        int row = 2;
        foreach (var item in rows)
        {
            SetText(sheet.Cell(row, 1), item.RouteCode);
            SetText(sheet.Cell(row, 2), item.RouteName);
            sheet.Cell(row, 3).SetValue(item.ActiveTickets);
            sheet.Cell(row, 4).SetValue(item.CheckedInTickets);
            sheet.Cell(row, 5).SetValue(item.CancelledTickets);
            sheet.Cell(row++, 6).SetValue(item.Revenue);
        }
        FinishTable(sheet, row, 6, 6);
    }

    private static void WritePayments(IXLWorksheet sheet, IReadOnlyList<RevenuePayment> rows)
    {
        Header(sheet, ["Phương thức ghi nhận", "Vé hiệu lực", "Doanh thu (VND)"], [35, 18, 23]);
        int row = 2;
        foreach (var item in rows)
        {
            SetText(sheet.Cell(row, 1), item.PaymentMethod);
            sheet.Cell(row, 2).SetValue(item.ActiveTickets);
            sheet.Cell(row++, 3).SetValue(item.Revenue);
        }
        FinishTable(sheet, row, 3, 3);
    }

    private static void WriteTrips(IXLWorksheet sheet, IReadOnlyList<RevenueTrip> rows)
    {
        Header(sheet, ["Mã chuyến", "Tuyến", "Khởi hành", "Biển số", "Sức chứa", "Vé hiệu lực",
            "Đã soát", "Đã hủy", "Doanh thu (VND)", "Chờ đánh dấu hoàn (VND)"],
            [22, 32, 22, 18, 15, 18, 16, 16, 23, 28]);
        int row = 2;
        foreach (var item in rows)
        {
            SetText(sheet.Cell(row, 1), item.TripCode);
            SetText(sheet.Cell(row, 2), item.RouteName);
            sheet.Cell(row, 3).SetValue(item.DepartureTime);
            SetText(sheet.Cell(row, 4), item.PlateNumber);
            sheet.Cell(row, 5).SetValue(item.Capacity);
            sheet.Cell(row, 6).SetValue(item.ActiveTickets);
            sheet.Cell(row, 7).SetValue(item.CheckedInTickets);
            sheet.Cell(row, 8).SetValue(item.CancelledTickets);
            sheet.Cell(row, 9).SetValue(item.Revenue);
            sheet.Cell(row++, 10).SetValue(item.PendingRefundValue);
        }
        sheet.Column(3).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        FinishTable(sheet, row, 10, 9, 10);
    }

    private static void WriteTickets(IXLWorksheet sheet, IReadOnlyList<RevenueTicket> rows)
    {
        Header(sheet, ["Mã vé", "Ngày bán", "Mã chuyến", "Tuyến", "Khách hàng", "Số điện thoại",
            "Thanh toán", "Trạng thái / hoàn tiền", "Giá vé (VND; vé hủy không tính doanh thu)", "Ngày đánh dấu hoàn"],
            [42, 22, 22, 32, 28, 20, 32, 34, 42, 24]);
        int row = 2;
        foreach (var item in rows)
        {
            SetText(sheet.Cell(row, 1), item.TicketCode);
            sheet.Cell(row, 2).SetValue(item.BookedAt);
            SetText(sheet.Cell(row, 3), item.TripCode);
            SetText(sheet.Cell(row, 4), item.RouteName);
            SetText(sheet.Cell(row, 5), item.CustomerName);
            SetText(sheet.Cell(row, 6), item.CustomerPhone);
            SetText(sheet.Cell(row, 7), item.PaymentMethod);
            SetText(sheet.Cell(row, 8), item.StatusText);
            sheet.Cell(row, 9).SetValue(item.Price);
            if (item.RefundedAt is { } refundedAt)
                sheet.Cell(row, 10).SetValue(refundedAt);
            row++;
        }
        sheet.Column(2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        sheet.Column(10).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
        FinishTable(sheet, row, 10, 9);
    }

    private static void Header(IXLWorksheet sheet, string[] labels, double[] widths, int row = 1)
    {
        for (int column = 1; column <= labels.Length; column++)
        {
            SetText(sheet.Cell(row, column), labels[column - 1]);
            sheet.Column(column).Width = widths[column - 1];
        }
        var header = sheet.Range(row, 1, row, labels.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#174A76");
        sheet.Row(row).Height = 28;
        sheet.SheetView.FreezeRows(row);
    }

    private static void FinishTable(IXLWorksheet sheet, int nextRow, int lastColumn, params int[] moneyColumns)
    {
        sheet.Range(1, 1, nextRow - 1, lastColumn).SetAutoFilter();
        foreach (int column in moneyColumns)
            sheet.Column(column).Style.NumberFormat.Format = "#,##0";
    }

    private static void SetText(IXLCell cell, string? text)
    {
        // User-entered strings must remain literal Excel text, never formulas.
        cell.Style.NumberFormat.Format = "@";
        cell.SetValue(text ?? string.Empty);
    }
}
