namespace HeThongQuanLyVeXeBus.Models;

public class Bus
{
    public int Id { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string BusModel { get; set; } = string.Empty; // e.g. Hyundai Universe, Thaco Mobihome
    public string BusTypeName { get; set; } = "Giường nằm 36 chỗ";
    public int TotalSeats { get; set; } = 36;
    public int NumberOfFloors { get; set; } = 2; // 1 hoặc 2
    public bool HasWifi { get; set; } = true;
    public bool HasAirConditioner { get; set; } = true;
    public bool IsActive { get; set; } = true;

    public virtual ICollection<Trip> Trips { get; set; } = new List<Trip>();

    public override string ToString() => $"{PlateNumber} - {BusTypeName} ({TotalSeats} chỗ)";
}
