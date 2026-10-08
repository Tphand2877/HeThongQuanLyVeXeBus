using System.Net.Mail;
using HeThongQuanLyVeXeBus.Data;
using HeThongQuanLyVeXeBus.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HeThongQuanLyVeXeBus.Services;

/// <summary>
/// Các thao tác tại quầy: nhân viên chỉ xác nhận vé sau khi đã thu/kiểm tra tiền thực tế.
/// Không xử lý hoặc tự xác nhận giao dịch qua cổng thanh toán.
/// </summary>
public sealed class BookingService(AppDbContext db)
{
    private readonly AppDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public Ticket Book(int tripId, string name, string phone, string? email,
        string pickup, string dropoff, string payment)
    {
        if (tripId <= 0)
            throw new ArgumentException("Vui lòng chọn chuyến xe hợp lệ.", nameof(tripId));

        var customerName = RequireText(name, "Tên khách hàng");
        var customerPhone = NormalizePhone(phone);
        var customerEmail = NormalizeEmail(email);
        var pickupPoint = RequireText(pickup, "Điểm đón");
        var dropoffPoint = RequireText(dropoff, "Điểm trả");
        if (string.Equals(pickupPoint, dropoffPoint, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Điểm đón và điểm trả phải khác nhau.", nameof(dropoff));

        var chosenPayment = RequireText(payment, "Phương thức thanh toán");
        string paymentMethod;
        if (string.Equals(chosenPayment, "Tiền mặt", StringComparison.OrdinalIgnoreCase))
            paymentMethod = "Tiền mặt";
        else if (string.Equals(chosenPayment, "Chuyển khoản (đã xác nhận)", StringComparison.OrdinalIgnoreCase))
            paymentMethod = "Chuyển khoản (đã xác nhận)";
        else
            throw new ArgumentException("Chỉ hỗ trợ tiền mặt hoặc chuyển khoản đã được nhân viên kiểm tra.", nameof(payment));

        Ticket? ticket = null;
        try
        {
            // SQLite's write transaction serializes the capacity check and insert with other bookings.
            using var transaction = _db.Database.BeginTransaction();
            var trip = _db.Trips.Include(t => t.Bus).Include(t => t.Route)
                .SingleOrDefault(t => t.Id == tripId)
                ?? throw new ArgumentException("Không tìm thấy chuyến xe.", nameof(tripId));
            // Re-read persisted state when the operator uses a long-lived context.
            _db.Entry(trip).Reload();
            if (_db.Entry(trip).State == EntityState.Detached)
                throw new ArgumentException("Không tìm thấy chuyến xe.", nameof(tripId));
            _db.Entry(trip.Bus).Reload();
            _db.Entry(trip.Route).Reload();
            if (trip.Status is not (TripStatus.Scheduled or TripStatus.Boarding) || trip.DepartureTime <= DateTime.Now)
                throw new InvalidOperationException("Chuyến xe đã khởi hành, đã hủy hoặc không còn nhận đặt vé.");
            if (trip.EstimatedArrivalTime <= trip.DepartureTime || trip.TicketPrice <= 0)
                throw new InvalidOperationException("Giờ đến hoặc giá vé của chuyến xe không hợp lệ.");
            if (!trip.Bus.IsActive || !trip.Route.IsActive)
                throw new InvalidOperationException("Xe hoặc tuyến đường không còn hoạt động.");
            if (trip.Bus.TotalSeats <= 0)
                throw new InvalidOperationException("Sức chứa của xe không hợp lệ.");
            var activeTickets = _db.Tickets.Count(t => t.TripId == tripId &&
                (t.Status == TicketStatus.Paid || t.Status == TicketStatus.CheckedIn));
            if (activeTickets >= trip.Bus.TotalSeats)
                throw new InvalidOperationException("Chuyến xe đã hết chỗ.");

            var now = DateTime.Now;
            var code = $"TK-{now:yyyyMMdd}-{Guid.NewGuid():N}".ToUpperInvariant();
            ticket = new Ticket
            {
                TicketCode = code,
                QrData = code,
                TripId = trip.Id,
                Trip = trip,
                SeatNumber = $"R-{Guid.NewGuid():N}",
                Floor = 1,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                CustomerEmail = customerEmail,
                PickupPoint = pickupPoint,
                DropoffPoint = dropoffPoint,
                Price = trip.TicketPrice,
                BookedAt = now,
                PaymentMethod = paymentMethod,
                Status = TicketStatus.Paid
            };
            _db.Tickets.Add(ticket);
            _db.SaveChanges();
            transaction.Commit();
            return ticket;
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqliteException { SqliteErrorCode: 19 })
        {
            DetachFailedInsert(ticket);
            throw new InvalidOperationException("Không thể lưu vé do mã đăng ký hoặc mã vé đã tồn tại. Vui lòng thử lại.", ex);
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqliteException { SqliteErrorCode: 5 or 6 })
        {
            DetachFailedInsert(ticket);
            throw new InvalidOperationException("Hệ thống đang xử lý giao dịch khác. Vui lòng thử lại.", ex);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            DetachFailedInsert(ticket);
            throw new InvalidOperationException("Không thể lưu vé do mã đăng ký hoặc mã vé đã tồn tại. Vui lòng thử lại.", ex);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            DetachFailedInsert(ticket);
            throw new InvalidOperationException("Hệ thống đang xử lý giao dịch khác. Vui lòng thử lại.", ex);
        }
    }

    public Ticket Cancel(string ticketCode, bool refunded = false)
    {
        var code = RequireText(ticketCode, "Mã vé").ToUpperInvariant();
        try
        {
            using var ownTransaction = _db.Database.CurrentTransaction is null
                ? _db.Database.BeginTransaction() : null;
            var ticket = _db.Tickets.Include(t => t.Trip).SingleOrDefault(t => t.TicketCode == code)
                ?? throw new ArgumentException("Không tìm thấy mã vé.", nameof(ticketCode));
            _db.Entry(ticket).Reload();
            if (_db.Entry(ticket).State == EntityState.Detached)
                throw new ArgumentException("Không tìm thấy mã vé.", nameof(ticketCode));
            if (ticket.Status != TicketStatus.Paid)
                throw new InvalidOperationException(ticket.Status == TicketStatus.CheckedIn
                    ? "Vé đã soát, không thể hủy." : "Vé này đã hủy hoặc không còn hợp lệ để hủy.");
            _db.Entry(ticket.Trip).Reload();
            if (ticket.Trip.DepartureTime <= DateTime.Now ||
                ticket.Trip.Status is not (TripStatus.Scheduled or TripStatus.Boarding))
                throw new InvalidOperationException("Chuyến đã khởi hành hoặc không còn nhận hủy vé.");

            ticket.Status = TicketStatus.Cancelled;
            ticket.RefundedAt = refunded ? DateTime.Now : null;
            _db.SaveChanges();
            ownTransaction?.Commit();
            return ticket;
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqliteException { SqliteErrorCode: 5 or 6 })
        {
            throw new InvalidOperationException("Hệ thống đang xử lý giao dịch khác. Vui lòng thử lại.", ex);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new InvalidOperationException("Hệ thống đang xử lý giao dịch khác. Vui lòng thử lại.", ex);
        }
    }

    public Ticket MarkRefunded(string ticketCode)
    {
        var code = RequireText(ticketCode, "Mã vé").ToUpperInvariant();
        try
        {
            using var ownTransaction = _db.Database.CurrentTransaction is null
                ? _db.Database.BeginTransaction() : null;
            var ticket = _db.Tickets.SingleOrDefault(t => t.TicketCode == code)
                ?? throw new ArgumentException("Không tìm thấy mã vé.", nameof(ticketCode));
            _db.Entry(ticket).Reload();
            if (_db.Entry(ticket).State == EntityState.Detached)
                throw new ArgumentException("Không tìm thấy mã vé.", nameof(ticketCode));
            if (ticket.Status != TicketStatus.Cancelled)
                throw new InvalidOperationException("Chỉ vé đã hủy mới có thể đánh dấu hoàn tiền.");
            if (ticket.RefundedAt.HasValue)
                throw new InvalidOperationException("Vé này đã được đánh dấu hoàn tiền.");

            ticket.RefundedAt = DateTime.Now;
            _db.SaveChanges();
            ownTransaction?.Commit();
            return ticket;
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqliteException { SqliteErrorCode: 5 or 6 })
        {
            throw new InvalidOperationException("Hệ thống đang xử lý giao dịch khác. Vui lòng thử lại.", ex);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new InvalidOperationException("Hệ thống đang xử lý giao dịch khác. Vui lòng thử lại.", ex);
        }
    }

    public Ticket CheckIn(string ticketCode)
    {
        var code = RequireText(ticketCode, "Mã vé").ToUpperInvariant();
        try
        {
            using var transaction = _db.Database.BeginTransaction();
            var ticket = _db.Tickets.Include(t => t.Trip).SingleOrDefault(t => t.TicketCode == code)
                ?? throw new ArgumentException("Không tìm thấy mã vé.", nameof(ticketCode));
            _db.Entry(ticket).Reload();
            if (_db.Entry(ticket).State == EntityState.Detached)
                throw new ArgumentException("Không tìm thấy mã vé.", nameof(ticketCode));
            _db.Entry(ticket.Trip).Reload();
            if (_db.Entry(ticket.Trip).State == EntityState.Detached)
                throw new InvalidOperationException("Chuyến xe của vé không còn tồn tại.");
            if (ticket.Status != TicketStatus.Paid)
                throw new InvalidOperationException(ticket.Status == TicketStatus.CheckedIn
                    ? "Vé đã được soát, không thể soát lại." : "Vé đã hủy, không thể lên xe.");
            if (!TripAcceptsCheckIn(ticket.Trip))
                throw new InvalidOperationException("Chuyến xe đã khởi hành, đã hủy hoặc không còn nhận soát vé.");

            ticket.Status = TicketStatus.CheckedIn;
            ticket.CheckedInAt = DateTime.Now;
            _db.SaveChanges();
            transaction.Commit();
            return ticket;
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqliteException { SqliteErrorCode: 5 or 6 })
        {
            throw new InvalidOperationException("Hệ thống đang xử lý giao dịch khác. Vui lòng thử lại.", ex);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new InvalidOperationException("Hệ thống đang xử lý giao dịch khác. Vui lòng thử lại.", ex);
        }
    }

    public TicketValidationResult Validate(string code)
    {
        var normalizedCode = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalizedCode))
            return Invalid("Vui lòng nhập hoặc quét mã vé.");

        var ticket = _db.Tickets.AsNoTracking()
            .Include(t => t.Trip).ThenInclude(t => t.Route)
            .Include(t => t.Trip).ThenInclude(t => t.Bus)
            .SingleOrDefault(t => t.TicketCode == normalizedCode);
        if (ticket is null)
            return Invalid("Không tìm thấy vé với mã này.");
        if (ticket.Status == TicketStatus.Cancelled)
            return Invalid("Vé đã hủy, không thể lên xe.", ticket);
        if (ticket.Status == TicketStatus.CheckedIn)
            return Invalid("Vé đã được soát, không thể soát lại.", ticket, "#F59E0B");
        if (ticket.Status != TicketStatus.Paid)
            return Invalid("Trạng thái vé không hợp lệ.", ticket);
        if (!TripAcceptsCheckIn(ticket.Trip))
            return Invalid("Chuyến xe đã khởi hành, đã hủy hoặc không còn nhận soát vé.", ticket);

        return new TicketValidationResult
        {
            IsValid = true,
            CanCheckIn = true,
            Message = "Vé đã thanh toán, hợp lệ để soát vé.",
            StatusBadgeColor = "#22C55E",
            Ticket = ticket
        };
    }

    private static bool TripAcceptsCheckIn(Trip trip) =>
        (trip.Status is TripStatus.Scheduled or TripStatus.Boarding) && trip.DepartureTime > DateTime.Now;

    private static TicketValidationResult Invalid(string message, Ticket? ticket = null, string color = "#EF4444") => new()
    {
        IsValid = false,
        CanCheckIn = false,
        Message = message,
        StatusBadgeColor = color,
        Ticket = ticket
    };

    private static string RequireText(string? text, string label)
    {
        var normalized = text?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException($"{label} không được để trống.");
        return normalized;
    }

    private static string NormalizePhone(string? phone)
    {
        var normalized = RequireText(phone, "Số điện thoại")
            .Replace(" ", "").Replace("-", "").Replace(".", "");
        if (normalized.StartsWith("+84", StringComparison.Ordinal))
            normalized = "0" + normalized[3..];
        if (normalized.Length is < 10 or > 11 || normalized[0] != '0' || !normalized.All(char.IsAsciiDigit))
            throw new ArgumentException("Số điện thoại phải gồm 10–11 chữ số, bắt đầu bằng 0 hoặc +84.", nameof(phone));
        return normalized;
    }

    private static string? NormalizeEmail(string? email)
    {
        var normalized = email?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > 254 || !MailAddress.TryCreate(normalized, out var address) ||
            !string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Email không hợp lệ.", nameof(email));
        return normalized;
    }

    private void DetachFailedInsert(Ticket? ticket)
    {
        if (ticket is not null)
            _db.Entry(ticket).State = EntityState.Detached;
    }
}
