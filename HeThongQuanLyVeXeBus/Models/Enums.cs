namespace HeThongQuanLyVeXeBus.Models;

public enum TripStatus
{
    Scheduled = 0,   // Sắp chạy / Chuẩn bị xuất bến
    Boarding = 1,    // Đang đón khách / Soát vé
    Departed = 2,    // Đã xuất bến
    Completed = 3,   // Hoàn thành chuyến
    Cancelled = 4    // Đã hủy
}

public enum TicketStatus
{
    Paid = 0,        // Đã thanh toán (Vé hợp lệ, chưa lên xe)
    CheckedIn = 1,   // Đã soát vé / Đã lên xe
    Cancelled = 2    // Đã hủy vé
}
