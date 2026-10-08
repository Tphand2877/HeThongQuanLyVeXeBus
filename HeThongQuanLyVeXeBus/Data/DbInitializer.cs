using HeThongQuanLyVeXeBus.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HeThongQuanLyVeXeBus.Data;

public static class DbInitializer
{
    public static void Initialize(AppDbContext context)
    {
        context.Database.EnsureCreated();
        // EnsureCreated does not update the schema of an existing SQLite database.
        // Upgrade it in place before any ticket queries or seed-data checks.
        using (var transaction = context.Database.BeginTransaction())
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "PRAGMA table_info(\"Tickets\")";
            bool hasRefundedAt = false;
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), nameof(Ticket.RefundedAt), StringComparison.OrdinalIgnoreCase))
                    {
                        hasRefundedAt = true;
                        break;
                    }
                }
            }

            if (!hasRefundedAt)
                context.Database.ExecuteSqlRaw("ALTER TABLE \"Tickets\" ADD COLUMN \"RefundedAt\" TEXT NULL");
            transaction.Commit();
        }

        // Kiểm tra xem đã có dữ liệu chưa
        if (context.Routes.Any())
        {
            return; // Đã khởi tạo
        }

        // 1. Tuyến xe liên tỉnh
        var routes = new List<BusRoute>
        {
            new()
            {
                RouteCode = "SG-DL",
                RouteName = "Sài Gòn - Đà Lạt",
                Origin = "TP. Hồ Chí Minh (BX Miền Đông)",
                Destination = "Đà Lạt (BX Liên Tỉnh)",
                DistanceKm = 305,
                EstimatedHours = 7.0,
                BasePrice = 280000m,
                IsActive = true
            },
            new()
            {
                RouteCode = "HN-SP",
                RouteName = "Hà Nội - Sa Pa",
                Origin = "Hà Nội (BX Mỹ Đình)",
                Destination = "Lào Cai - Sa Pa (BX Sa Pa)",
                DistanceKm = 315,
                EstimatedHours = 5.5,
                BasePrice = 320000m,
                IsActive = true
            },
            new()
            {
                RouteCode = "SG-NT",
                RouteName = "Sài Gòn - Nha Trang",
                Origin = "TP. Hồ Chí Minh (BX Miền Đông)",
                Destination = "Nha Trang (BX Phía Nam)",
                DistanceKm = 430,
                EstimatedHours = 8.5,
                BasePrice = 310000m,
                IsActive = true
            },
            new()
            {
                RouteCode = "HN-HP",
                RouteName = "Hà Nội - Hải Phòng",
                Origin = "Hà Nội (BX Nước Ngầm)",
                Destination = "Hải Phòng (BX Vĩnh Niệm)",
                DistanceKm = 120,
                EstimatedHours = 2.0,
                BasePrice = 130000m,
                IsActive = true
            },
            new()
            {
                RouteCode = "DN-QN",
                RouteName = "Đà Nẵng - Quy Nhơn",
                Origin = "Đà Nẵng (BX Trung Tâm)",
                Destination = "Quy Nhơn (BX Quy Nhơn)",
                DistanceKm = 300,
                EstimatedHours = 6.0,
                BasePrice = 250000m,
                IsActive = true
            }
        };
        context.Routes.AddRange(routes);
        context.SaveChanges();

        // 2. Xe khách
        var buses = new List<Bus>
        {
            new()
            {
                PlateNumber = "51B-294.88",
                BusModel = "Thaco Mobihome King",
                BusTypeName = "Giường nằm 36 chỗ",
                TotalSeats = 36,
                NumberOfFloors = 2,
                HasWifi = true,
                HasAirConditioner = true,
                IsActive = true
            },
            new()
            {
                PlateNumber = "51B-302.15",
                BusModel = "Hyundai Universe Luxury",
                BusTypeName = "Giường nằm 36 chỗ",
                TotalSeats = 36,
                NumberOfFloors = 2,
                HasWifi = true,
                HasAirConditioner = true,
                IsActive = true
            },
            new()
            {
                PlateNumber = "29B-188.92",
                BusModel = "Tracomeco Limousine",
                BusTypeName = "Limousine VIP 34 chỗ",
                TotalSeats = 34,
                NumberOfFloors = 2,
                HasWifi = true,
                HasAirConditioner = true,
                IsActive = true
            },
            new()
            {
                PlateNumber = "29B-665.41",
                BusModel = "Samco Felix",
                BusTypeName = "Ghế ngồi cao cấp 28 chỗ",
                TotalSeats = 28,
                NumberOfFloors = 1,
                HasWifi = true,
                HasAirConditioner = true,
                IsActive = true
            }
        };
        context.Buses.AddRange(buses);
        context.SaveChanges();

        // 3. Người dùng
        var users = new List<User>
        {
            new() { Username = "admin", FullName = "Quản Trị Viên Hệ Thống", Role = "Quản trị viên" },
            new() { Username = "thungan", FullName = "Nguyễn Văn Thu Ngân", Role = "Nhân viên bán vé" },
            new() { Username = "soatve", FullName = "Trần Kiểm Soát", Role = "Kiểm soát viên" }
        };
        context.Users.AddRange(users);
        context.SaveChanges();

        // 4. Chuyến mẫu cho ngày mai để luôn có chuyến có thể bán vé khi khởi tạo mới.
        var today = DateTime.Today.AddDays(1);
        var trips = new List<Trip>
        {
            new()
            {
                TripCode = $"CX-{today:yyyyMMdd}-01",
                RouteId = routes[0].Id, // SG - DL
                BusId = buses[0].Id,
                DriverName = "Nguyễn Văn Hùng",
                DriverPhone = "0908123456",
                DepartureTime = today.AddHours(8).AddMinutes(0), // 08:00
                EstimatedArrivalTime = today.AddHours(15).AddMinutes(0),
                TicketPrice = 280000m,
                Status = TripStatus.Scheduled
            },
            new()
            {
                TripCode = $"CX-{today:yyyyMMdd}-02",
                RouteId = routes[0].Id, // SG - DL
                BusId = buses[1].Id,
                DriverName = "Lê Hoàng Nam",
                DriverPhone = "0912345678",
                DepartureTime = today.AddHours(13).AddMinutes(30), // 13:30
                EstimatedArrivalTime = today.AddHours(20).AddMinutes(30),
                TicketPrice = 280000m,
                Status = TripStatus.Scheduled
            },
            new()
            {
                TripCode = $"CX-{today:yyyyMMdd}-03",
                RouteId = routes[1].Id, // HN - SP
                BusId = buses[2].Id,
                DriverName = "Phạm Quốc Tuấn",
                DriverPhone = "0987654321",
                DepartureTime = today.AddHours(9).AddMinutes(15), // 09:15
                EstimatedArrivalTime = today.AddHours(14).AddMinutes(45),
                TicketPrice = 320000m,
                Status = TripStatus.Scheduled
            },
            new()
            {
                TripCode = $"CX-{today:yyyyMMdd}-04",
                RouteId = routes[2].Id, // SG - NT
                BusId = buses[0].Id,
                DriverName = "Đỗ Minh Trí",
                DriverPhone = "0933112233",
                DepartureTime = today.AddHours(21).AddMinutes(0), // 21:00
                EstimatedArrivalTime = today.AddDays(1).AddHours(5).AddMinutes(30),
                TicketPrice = 310000m,
                Status = TripStatus.Scheduled
            },
            new()
            {
                TripCode = $"CX-{today:yyyyMMdd}-05",
                RouteId = routes[3].Id, // HN - HP
                BusId = buses[3].Id,
                DriverName = "Vũ Anh Dũng",
                DriverPhone = "0944556677",
                DepartureTime = today.AddHours(10).AddMinutes(0), // 10:00
                EstimatedArrivalTime = today.AddHours(12).AddMinutes(0),
                TicketPrice = 130000m,
                Status = TripStatus.Scheduled
            }
        };
        context.Trips.AddRange(trips);
        context.SaveChanges();

        // 5. Vé mẫu để test ngay lập tức các trạng thái
        var trip1 = trips[0]; // Chuyến SG - DL 08:00
        var tickets = new List<Ticket>
        {
            new()
            {
                TicketCode = $"TK-{today:yyyyMMdd}-001",
                TripId = trip1.Id,
                SeatNumber = "A01",
                Floor = 1,
                CustomerName = "Trần Thị Mai",
                CustomerPhone = "0901234567",
                CustomerEmail = "maitt@example.com",
                PickupPoint = "Bến xe Miền Đông Mới",
                DropoffPoint = "Bến xe Liên Tỉnh Đà Lạt",
                Price = trip1.TicketPrice,
                BookedAt = DateTime.Now.AddHours(-3),
                PaymentMethod = "Chuyển khoản (đã xác nhận)",
                Status = TicketStatus.Paid,
                QrData = $"TK-{today:yyyyMMdd}-001"
            },
            new()
            {
                TicketCode = $"TK-{today:yyyyMMdd}-002",
                TripId = trip1.Id,
                SeatNumber = "A02",
                Floor = 1,
                CustomerName = "Nguyễn Hữu Đạt",
                CustomerPhone = "0918765432",
                CustomerEmail = "datnh@example.com",
                PickupPoint = "Bến xe Miền Đông Mới",
                DropoffPoint = "Bảo Lộc - Lâm Đồng",
                Price = trip1.TicketPrice,
                BookedAt = DateTime.Now.AddHours(-2),
                PaymentMethod = "Tiền mặt",
                Status = TicketStatus.Paid,
                CheckedInAt = null,
                QrData = $"TK-{today:yyyyMMdd}-002"
            },
            new()
            {
                TicketCode = $"TK-{today:yyyyMMdd}-003",
                TripId = trip1.Id,
                SeatNumber = "B01",
                Floor = 2,
                CustomerName = "Lê Hoàng Phúc",
                CustomerPhone = "0989988776",
                PickupPoint = "Ngã 4 Thủ Đức",
                DropoffPoint = "Đà Lạt",
                Price = trip1.TicketPrice,
                BookedAt = DateTime.Now.AddHours(-1),
                PaymentMethod = "Tiền mặt",
                Status = TicketStatus.Cancelled,
                Notes = "Khách bận việc đột xuất hủy trước 2 tiếng",
                QrData = $"TK-{today:yyyyMMdd}-003"
            }
        };
        context.Tickets.AddRange(tickets);
        context.SaveChanges();
    }
}
