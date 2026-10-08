namespace HeThongQuanLyVeXeBus.Models;

public class Trip
{
    public int Id { get; set; }
    public string TripCode { get; set; } = string.Empty; // CX-YYYYMMDD-XX
    public int RouteId { get; set; }
    public virtual BusRoute Route { get; set; } = null!;

    public int BusId { get; set; }
    public virtual Bus Bus { get; set; } = null!;

    public string DriverName { get; set; } = string.Empty;
    public string DriverPhone { get; set; } = string.Empty;

    public DateTime DepartureTime { get; set; }
    public DateTime EstimatedArrivalTime { get; set; }

    public decimal TicketPrice { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Scheduled;

    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();

    public string FormattedDeparture => DepartureTime.ToString("HH:mm - dd/MM/yyyy");
    public string DisplaySummary => $"{TripCode} | {DepartureTime:HH:mm dd/MM} | {Route?.Origin} → {Route?.Destination}";
}
