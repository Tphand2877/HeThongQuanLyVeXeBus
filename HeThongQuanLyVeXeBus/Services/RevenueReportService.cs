using HeThongQuanLyVeXeBus.Data;
using HeThongQuanLyVeXeBus.Models;
using Microsoft.EntityFrameworkCore;

namespace HeThongQuanLyVeXeBus.Services;

public sealed class RevenueReportService(AppDbContext db)
{
    public RevenueReport Generate(DateTime from, DateTime to, int? routeId = null)
    {
        var start = from.Date;
        var end = to.Date;
        if (start > end)
            throw new ArgumentException("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.", nameof(from));
        var dayCount = (end - start).Days + 1;
        var granularity = dayCount <= 31 ? "Ngày" : dayCount <= 365 ? "Tuần" : "Tháng";

        var query = db.Tickets.AsNoTracking()
            .Include(ticket => ticket.Trip).ThenInclude(trip => trip.Route)
            .Include(ticket => ticket.Trip).ThenInclude(trip => trip.Bus)
            .Where(ticket => ticket.BookedAt >= start);

        // An exclusive upper bound avoids relying on the precision of persisted timestamps.
        // DateTime.MaxValue.Date cannot be advanced by one day.
        if (end < DateTime.MaxValue.Date)
        {
            var endExclusive = end.AddDays(1);
            query = query.Where(ticket => ticket.BookedAt < endExclusive);
        }

        if (routeId.HasValue)
            query = query.Where(ticket => ticket.Trip.RouteId == routeId.Value);

        var tickets = query.ToList();
        var totals = new TicketTotals();
        var trendTotals = new Dictionary<int, TicketTotals>();
        var routeTotals = new Dictionary<int, GroupTotals>();
        var paymentTotals = new Dictionary<string, TicketTotals>(StringComparer.Ordinal);
        var tripTotals = new Dictionary<int, GroupTotals>();
        var startDayNumber = DateOnly.FromDateTime(start).DayNumber;
        var firstMonday = startDayNumber - MondayOffset(start.DayOfWeek);

        foreach (var ticket in tickets)
        {
            totals.Add(ticket);

            var booked = ticket.BookedAt;
            var dayNumber = DateOnly.FromDateTime(booked).DayNumber;
            var trendKey = granularity switch
            {
                "Ngày" => dayNumber,
                "Tuần" => dayNumber - MondayOffset(booked.DayOfWeek),
                _ => booked.Year * 12 + booked.Month
            };
            if (!trendTotals.TryGetValue(trendKey, out var trend))
                trendTotals.Add(trendKey, trend = new TicketTotals());
            trend.Add(ticket);

            if (!routeTotals.TryGetValue(ticket.Trip.RouteId, out var route))
                routeTotals.Add(ticket.Trip.RouteId, route = new GroupTotals(ticket));
            route.Totals.Add(ticket);

            if (ticket.Status is TicketStatus.Paid or TicketStatus.CheckedIn)
            {
                if (!paymentTotals.TryGetValue(ticket.PaymentMethod, out var payment))
                    paymentTotals.Add(ticket.PaymentMethod, payment = new TicketTotals());
                payment.Add(ticket);
            }

            if (!tripTotals.TryGetValue(ticket.TripId, out var trip))
                tripTotals.Add(ticket.TripId, trip = new GroupTotals(ticket));
            trip.Totals.Add(ticket);
        }

        var trendRows = new List<RevenueTrend>();
        var endDayNumber = DateOnly.FromDateTime(end).DayNumber;
        if (granularity == "Ngày")
        {
            for (var day = startDayNumber; day <= endDayNumber; day++)
                trendRows.Add(TrendRow(day, DateOnly.FromDayNumber(day).ToString("dd/MM/yyyy")));
        }
        else if (granularity == "Tuần")
        {
            for (var monday = firstMonday; monday <= endDayNumber; monday += 7)
            {
                var visibleStart = DateOnly.FromDayNumber(Math.Max(monday, startDayNumber));
                var visibleEnd = DateOnly.FromDayNumber(Math.Min(monday + 6, endDayNumber));
                trendRows.Add(TrendRow(monday, $"{visibleStart:dd/MM} - {visibleEnd:dd/MM/yyyy}"));
            }
        }
        else
        {
            var year = start.Year;
            var month = start.Month;
            while (true)
            {
                trendRows.Add(TrendRow(year * 12 + month, $"{month:00}/{year:0000}"));
                if (year == end.Year && month == end.Month)
                    break;
                if (++month == 13)
                {
                    month = 1;
                    year++;
                }
            }
        }

        var routes = routeTotals.Values
            .Select(group => new RevenueRoute(
                group.Ticket.Trip.Route.RouteCode,
                group.Ticket.Trip.Route.RouteName,
                group.Totals.ActiveCount,
                group.Totals.CheckedInCount,
                group.Totals.CancelledCount,
                group.Totals.Revenue))
            .OrderByDescending(row => row.Revenue)
            .ThenBy(row => row.RouteCode, StringComparer.Ordinal)
            .ToList();

        var payments = paymentTotals
            .Select(pair => new RevenuePayment(pair.Key, pair.Value.ActiveCount, pair.Value.Revenue))
            .OrderByDescending(row => row.Revenue)
            .ThenBy(row => row.PaymentMethod, StringComparer.Ordinal)
            .ToList();

        var trips = tripTotals.Values
            .Select(group => new RevenueTrip(
                group.Ticket.Trip.TripCode,
                group.Ticket.Trip.Route.RouteName,
                group.Ticket.Trip.DepartureTime,
                group.Ticket.Trip.Bus.PlateNumber,
                group.Ticket.Trip.Bus.TotalSeats,
                group.Totals.ActiveCount,
                group.Totals.CheckedInCount,
                group.Totals.CancelledCount,
                group.Totals.Revenue,
                group.Totals.PendingRefundValue))
            .OrderByDescending(row => row.Revenue)
            .ThenBy(row => row.DepartureTime)
            .ThenBy(row => row.TripCode, StringComparer.Ordinal)
            .ToList();

        var details = tickets
            .OrderByDescending(ticket => ticket.BookedAt)
            .ThenBy(ticket => ticket.TicketCode, StringComparer.Ordinal)
            .Select(ticket => new RevenueTicket(
                ticket.TicketCode,
                ticket.BookedAt,
                ticket.Trip.TripCode,
                ticket.Trip.Route.RouteName,
                ticket.CustomerName,
                ticket.CustomerPhone,
                ticket.PaymentMethod,
                ticket.StatusText,
                ticket.Price,
                ticket.RefundedAt))
            .ToList();

        return new RevenueReport(start, end, granularity,
            totals.TicketCount, totals.ActiveCount, totals.CheckedInCount,
            totals.CancelledCount, totals.RefundedCount, totals.PendingRefundCount,
            totals.Revenue, totals.CancelledValue, totals.RefundedValue, totals.PendingRefundValue,
            trendRows, routes, payments, trips, details);

        RevenueTrend TrendRow(int key, string label)
        {
            trendTotals.TryGetValue(key, out var value);
            return new RevenueTrend(label, value?.Revenue ?? 0m,
                value?.ActiveCount ?? 0, value?.CancelledCount ?? 0);
        }
    }

    private static int MondayOffset(DayOfWeek day) => ((int)day + 6) % 7;

    private sealed class GroupTotals(Ticket ticket)
    {
        public Ticket Ticket { get; } = ticket;
        public TicketTotals Totals { get; } = new();
    }

    private sealed class TicketTotals
    {
        public int TicketCount { get; private set; }
        public int ActiveCount { get; private set; }
        public int CheckedInCount { get; private set; }
        public int CancelledCount { get; private set; }
        public int RefundedCount { get; private set; }
        public int PendingRefundCount { get; private set; }
        public decimal Revenue { get; private set; }
        public decimal CancelledValue { get; private set; }
        public decimal RefundedValue { get; private set; }
        public decimal PendingRefundValue { get; private set; }

        public void Add(Ticket ticket)
        {
            TicketCount++;
            switch (ticket.Status)
            {
                case TicketStatus.Paid:
                    ActiveCount++;
                    Revenue += ticket.Price;
                    break;
                case TicketStatus.CheckedIn:
                    ActiveCount++;
                    CheckedInCount++;
                    Revenue += ticket.Price;
                    break;
                case TicketStatus.Cancelled:
                    CancelledCount++;
                    CancelledValue += ticket.Price;
                    if (ticket.RefundedAt.HasValue)
                    {
                        RefundedCount++;
                        RefundedValue += ticket.Price;
                    }
                    else
                    {
                        PendingRefundCount++;
                        PendingRefundValue += ticket.Price;
                    }
                    break;
            }
        }
    }
}

public sealed record RevenueReport(
    DateTime From, DateTime To, string TrendGranularity,
    int TicketCount, int ActiveCount, int CheckedInCount, int CancelledCount,
    int RefundedCount, int PendingRefundCount,
    decimal Revenue, decimal CancelledValue, decimal RefundedValue, decimal PendingRefundValue,
    IReadOnlyList<RevenueTrend> Trend, IReadOnlyList<RevenueRoute> Routes,
    IReadOnlyList<RevenuePayment> Payments, IReadOnlyList<RevenueTrip> Trips,
    IReadOnlyList<RevenueTicket> Tickets);

public sealed record RevenueTrend(string Label, decimal Revenue, int ActiveTickets, int CancelledTickets);

public sealed record RevenueRoute(string RouteCode, string RouteName, int ActiveTickets,
    int CheckedInTickets, int CancelledTickets, decimal Revenue);

public sealed record RevenuePayment(string PaymentMethod, int ActiveTickets, decimal Revenue);

public sealed record RevenueTrip(string TripCode, string RouteName, DateTime DepartureTime,
    string PlateNumber, int Capacity, int ActiveTickets, int CheckedInTickets,
    int CancelledTickets, decimal Revenue, decimal PendingRefundValue);

public sealed record RevenueTicket(string TicketCode, DateTime BookedAt, string TripCode,
    string RouteName, string CustomerName, string CustomerPhone, string PaymentMethod,
    string StatusText, decimal Price, DateTime? RefundedAt);
