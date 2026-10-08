namespace HeThongQuanLyVeXeBus.Models;

public class Ticket
{
    public int Id { get; set; }
    public string TicketCode { get; set; } = string.Empty; // Mã vé tra cứu & QR (TK-20261008-XXXX)
    
    public int TripId { get; set; }
    public virtual Trip Trip { get; set; } = null!;

    // Legacy persistence columns: existing tickets retain their old seat/floor values.
    // New tickets use an opaque registration token and Floor = 1; neither assigns a physical place.
    public string SeatNumber { get; set; } = string.Empty;
    public int Floor { get; set; } = 1;

    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }

    public string PickupPoint { get; set; } = string.Empty;
    public string DropoffPoint { get; set; } = string.Empty;

    public decimal Price { get; set; }
    public DateTime BookedAt { get; set; } = DateTime.Now;
    public string PaymentMethod { get; set; } = "Tiền mặt";

    public TicketStatus Status { get; set; } = TicketStatus.Paid;
    public DateTime? CheckedInAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Chuỗi dữ liệu mã hóa trong QR Code (có thể là mã vé hoặc chuỗi xác thực)
    /// </summary>
    public string QrData { get; set; } = string.Empty;

    public string StatusText => Status switch
    {
        TicketStatus.Paid => "Đã thanh toán (Hợp lệ)",
        TicketStatus.CheckedIn => "Đã soát vé (Lên xe)",
        TicketStatus.Cancelled => RefundedAt.HasValue ? "Đã hủy vé (đã hoàn tiền)" : "Đã hủy vé (chưa hoàn tiền)",
        _ => "Không xác định"
    };
}
