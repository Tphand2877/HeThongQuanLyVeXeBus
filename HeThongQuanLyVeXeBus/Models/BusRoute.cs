namespace HeThongQuanLyVeXeBus.Models;

public class BusRoute
{
    public int Id { get; set; }
    public string RouteCode { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public double DistanceKm { get; set; }
    public double EstimatedHours { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();

    public override string ToString() => $"{RouteCode} - {RouteName} ({Origin} → {Destination})";
}
