namespace HeThongQuanLyVeXeBus.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Nhân viên"; // "Quản trị viên", "Nhân viên bán vé", "Kiểm soát viên"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class TicketValidationResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public string StatusBadgeColor { get; set; } = "#EF4444"; // Red or Green or Amber
    public Ticket? Ticket { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.Now;
    public bool CanCheckIn { get; set; }
}
