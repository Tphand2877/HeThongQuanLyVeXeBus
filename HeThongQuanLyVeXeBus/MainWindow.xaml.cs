using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using HeThongQuanLyVeXeBus.Data;
using HeThongQuanLyVeXeBus.Models;
using HeThongQuanLyVeXeBus.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Win32;

namespace HeThongQuanLyVeXeBus;

public partial class MainWindow : Window
{
	public sealed record StatusChoice(TripStatus? Value, string Label);

	public sealed record TripRow(Trip Trip, string StatusText);
	public sealed record ReportRouteChoice(int? Id, string Label);
	private sealed record TrendBar(string Label, string AmountText, double BarHeight, string TooltipText);
	private sealed record BreakdownBar(string Label, string AmountText, double BarWidth, string TooltipText);

	private static readonly StatusChoice[] Statuses = new StatusChoice[5]
	{
		new StatusChoice(TripStatus.Scheduled, "Sắp chạy"),
		new StatusChoice(TripStatus.Boarding, "Đang đón khách"),
		new StatusChoice(TripStatus.Departed, "Đã xuất bến"),
		new StatusChoice(TripStatus.Completed, "Hoàn thành"),
		new StatusChoice(TripStatus.Cancelled, "Đã hủy")
	};

	private List<Trip> _trips = new List<Trip>();

	private List<Ticket> _tickets = new List<Ticket>();

	private bool _ready;

	private bool _loading;

	private string? _pdfTicketCode;

	public MainWindow()
	{
		InitializeComponent();
		DepartureDateBox.SelectedDate = DateTime.Today.AddDays(1.0);
		ArrivalDateBox.SelectedDate = DateTime.Today.AddDays(1.0);
		TripFilterStatus.ItemsSource = new StatusChoice[1]
		{
			new StatusChoice(null, "Tất cả")
		}.Concat(Statuses).ToList();
		TripFilterStatus.SelectedIndex = 0;
		TripStatusBox.ItemsSource = Statuses;
		ReportFromDate.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
		ReportToDate.SelectedDate = DateTime.Today;
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			InitializeDatabase();
			RefreshData();
			Notify("Đã tải dữ liệu.");
		});
	}

	private void InitializeDatabase()
	{
		using AppDbContext context = new AppDbContext();
		DbInitializer.Initialize(context);
		_ready = true;
	}

	private void Run(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			string text = ((ex.InnerException == null) ? ex.Message : (ex.Message + "\n" + ex.GetBaseException().Message));
			StatusMessage.Text = "Lỗi: " + text;
			MessageBox.Show(this, text, "Không thể thực hiện", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void Notify(string message)
	{
		StatusMessage.Text = message;
	}

	private static string Required(TextBox box, string label)
	{
		string text = box.Text.Trim();
		if (text.Length == 0)
		{
			box.Focus();
			throw new ArgumentException("Vui lòng nhập " + label + ".");
		}
		return text;
	}

	private static decimal PositiveMoney(TextBox box, string label)
	{
		string s = Required(box, label);
		if ((!decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out var result) && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result)) || result <= 0m)
		{
			throw new ArgumentException(label + " phải là số tiền lớn hơn 0.");
		}
		return result;
	}

	private static double PositiveDouble(TextBox box, string label)
	{
		string s = Required(box, label);
		if ((!double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) && !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) || !double.IsFinite(result) || result <= 0.0)
		{
			throw new ArgumentException(label + " phải là số lớn hơn 0.");
		}
		return result;
	}

	private static DateTime EnteredDateTime(DatePicker date, TextBox time, string label)
	{
		DateTime? selectedDate = date.SelectedDate;
		if (selectedDate.HasValue)
		{
			DateTime valueOrDefault = selectedDate.GetValueOrDefault();
			if (TimeSpan.TryParseExact(time.Text.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out var result) && !(result < TimeSpan.Zero) && !(result >= TimeSpan.FromDays(1)))
			{
				return valueOrDefault.Date.Add(result);
			}
		}
		throw new ArgumentException("Vui lòng nhập " + label + " đúng ngày và giờ HH:mm.");
	}

	private void Refresh_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			if (!_ready)
			{
				InitializeDatabase();
			}
			RefreshData();
			Notify("Đã cập nhật dữ liệu.");
		});
	}

	private void RefreshData()
	{
		int? routeId = (RoutesGrid.SelectedItem as BusRoute)?.Id;
		int? busId = (BusesGrid.SelectedItem as Bus)?.Id;
		int? tripId = (TripsGrid.SelectedItem as TripRow)?.Trip.Id;
		int? bookingId = (BookingTripBox.SelectedItem as Trip)?.Id;
		int? ticketId = (TicketsGrid.SelectedItem as Ticket)?.Id;
		int? reportRouteId = (ReportRouteBox.SelectedItem as ReportRouteChoice)?.Id;
		using AppDbContext appDbContext = new AppDbContext();
		List<BusRoute> list = (from r in appDbContext.Routes.AsNoTracking()
			orderby r.RouteCode
			select r).ToList();
		List<Bus> list2 = (from b in appDbContext.Buses.AsNoTracking()
			orderby b.PlateNumber
			select b).ToList();
		_trips = (from t in appDbContext.Trips.AsNoTracking().Include((Trip t) => t.Route).Include((Trip t) => t.Bus)
			orderby t.DepartureTime descending
			select t).ToList();
		_tickets = (from t in appDbContext.Tickets.AsNoTracking().Include((Ticket t) => t.Trip).ThenInclude((Trip t) => t.Route)
			orderby t.BookedAt descending
			select t).ToList();
		List<Ticket> list3 = _tickets.Where((Ticket t) =>
		{
			TicketStatus status = t.Status;
			return (uint)status <= 1u;
		}).ToList();
		_loading = true;
		try
		{
			RoutesGrid.ItemsSource = list;
			BusesGrid.ItemsSource = list2;
			TripRouteBox.ItemsSource = list;
			TripBusBox.ItemsSource = list2;
			ReportRouteBox.ItemsSource = new[] { new ReportRouteChoice(null, "Tất cả tuyến") }
				.Concat(list.Select(route => new ReportRouteChoice(route.Id, route.RouteName))).ToList();
			ReportRouteBox.SelectedItem = (ReportRouteBox.ItemsSource as IEnumerable<ReportRouteChoice>)?
				.FirstOrDefault(choice => choice.Id == reportRouteId);
			BookingTripBox.ItemsSource = (from t in _trips.Where((Trip t) =>
				{
					TripStatus status = t.Status;
					bool flag = (uint)status <= 1u;
					return flag && t.DepartureTime > DateTime.Now && t.Route.IsActive && t.Bus.IsActive;
				})
				orderby t.DepartureTime
				select t).ToList();
			RoutesGrid.SelectedItem = list.FirstOrDefault((BusRoute r) => r.Id == routeId);
			BusesGrid.SelectedItem = list2.FirstOrDefault((Bus b) => b.Id == busId);
			TripRouteBox.SelectedItem = list.FirstOrDefault((BusRoute r) => r.Id == (TripsGrid.SelectedItem as TripRow)?.Trip.RouteId);
			TripBusBox.SelectedItem = list2.FirstOrDefault((Bus b) => b.Id == (TripsGrid.SelectedItem as TripRow)?.Trip.BusId);
			BookingTripBox.SelectedItem = (BookingTripBox.ItemsSource as IEnumerable<Trip>)?.FirstOrDefault((Trip t) => t.Id == bookingId);
			RouteCount.Text = list.Count.ToString("N0");
			BusCount.Text = list2.Count((Bus b) => b.IsActive).ToString("N0");
			TripCount.Text = _trips.Count((Trip t) =>
			{
				TripStatus status = t.Status;
				return (uint)status <= 1u && t.DepartureTime > DateTime.Now;
			}).ToString("N0");
			TicketCount.Text = list3.Count.ToString("N0");
			RevenueTotal.Text = $"{list3.Sum((Ticket t) => t.Price):N0} ₫";
			FilterTrips();
			TripsGrid.SelectedItem = (TripsGrid.ItemsSource as IEnumerable<TripRow>)?.FirstOrDefault((TripRow t) => t.Trip.Id == tripId);
			FilterTickets();
			TicketsGrid.SelectedItem = (TicketsGrid.ItemsSource as IEnumerable<Ticket>)?.FirstOrDefault((Ticket t) => t.Id == ticketId);
		}
		finally
		{
			_loading = false;
		}
		RoutesGrid_SelectionChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
		BusesGrid_SelectionChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
		UpdateBookingTrip();
		TripsGrid_SelectionChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
		TicketsGrid_SelectionChanged(this, new SelectionChangedEventArgs(Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
		if (MainTabs.SelectedIndex == 6)
			RefreshRevenueReport();
	}

	private void AddRoute_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			string code = Required(RouteCodeBox, "mã tuyến");
			string text = Required(RouteNameBox, "tên tuyến");
			string text2 = Required(OriginBox, "điểm đi");
			string text3 = Required(DestinationBox, "điểm đến");
			if (string.Equals(text2, text3, StringComparison.OrdinalIgnoreCase))
			{
				throw new ArgumentException("Điểm đi và điểm đến phải khác nhau.");
			}
			double num = PositiveDouble(DistanceBox, "Quãng đường");
			double num2 = PositiveDouble(DurationBox, "Thời gian dự kiến");
			decimal num3 = PositiveMoney(BasePriceBox, "Giá cơ bản");
			int? id = (RoutesGrid.SelectedItem as BusRoute)?.Id;
			using AppDbContext appDbContext = new AppDbContext();
			if (appDbContext.Routes.Any((BusRoute r) => (int?)r.Id != id && r.RouteCode.ToUpper() == code.ToUpper()))
			{
				throw new InvalidOperationException("Mã tuyến đã tồn tại.");
			}
			BusRoute busRoute;
			if (id.HasValue)
			{
				int selected = id.GetValueOrDefault();
				busRoute = appDbContext.Routes.Single((BusRoute r) => r.Id == selected);
			}
			else
			{
				busRoute = new BusRoute();
			}
			BusRoute route = busRoute;
			if (id.HasValue && appDbContext.Tickets.Any((Ticket t) => (int?)t.Trip.RouteId == id) && (route.RouteCode != code || route.RouteName != text || route.Origin != text2 || route.Destination != text3 || route.DistanceKm != num || route.EstimatedHours != num2 || route.BasePrice != num3))
			{
				throw new InvalidOperationException("Tuyến đã có vé bán: chỉ được thay đổi trạng thái hoạt động.");
			}
			route.RouteCode = code;
			route.RouteName = text;
			route.Origin = text2;
			route.Destination = text3;
			route.DistanceKm = num;
			route.EstimatedHours = num2;
			route.BasePrice = num3;
			route.IsActive = RouteActiveBox.IsChecked == true;
			if (!id.HasValue)
			{
				appDbContext.Routes.Add(route);
			}
			appDbContext.SaveChanges();
			RefreshData();
			RoutesGrid.SelectedItem = (RoutesGrid.ItemsSource as IEnumerable<BusRoute>)?.FirstOrDefault((BusRoute r) => r.Id == route.Id);
			Notify("Đã lưu tuyến xe.");
		});
	}

	private void RoutesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_loading && RoutesGrid.SelectedItem is BusRoute busRoute)
		{
			RouteCodeBox.Text = busRoute.RouteCode;
			RouteNameBox.Text = busRoute.RouteName;
			OriginBox.Text = busRoute.Origin;
			DestinationBox.Text = busRoute.Destination;
			DistanceBox.Text = busRoute.DistanceKm.ToString(CultureInfo.CurrentCulture);
			DurationBox.Text = busRoute.EstimatedHours.ToString(CultureInfo.CurrentCulture);
			BasePriceBox.Text = busRoute.BasePrice.ToString(CultureInfo.CurrentCulture);
			RouteActiveBox.IsChecked = busRoute.IsActive;
		}
	}

	private void NewRoute_Click(object sender, RoutedEventArgs e)
	{
		RoutesGrid.SelectedItem = null;
		RouteCodeBox.Clear();
		RouteNameBox.Clear();
		OriginBox.Clear();
		DestinationBox.Clear();
		DistanceBox.Clear();
		DurationBox.Clear();
		BasePriceBox.Clear();
		RouteActiveBox.IsChecked = true;
		RouteCodeBox.Focus();
	}

	private void AddBus_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			string plate = Required(PlateBox, "biển số");
			string text = Required(BusModelBox, "dòng xe");
			string text2 = Required(BusTypeBox, "loại xe");
			if (!int.TryParse(Required(CapacityBox, "số chỗ"), out var result) || result <= 0 || result > 500)
			{
				throw new ArgumentException("Số chỗ phải từ 1 đến 500.");
			}
			int num = FloorsBox.SelectedIndex + 1;
			if ((uint)(num - 1) > 1u)
			{
				throw new ArgumentException("Chọn số tầng của xe.");
			}
			int? id = (BusesGrid.SelectedItem as Bus)?.Id;
			using AppDbContext appDbContext = new AppDbContext();
			if (appDbContext.Buses.Any((Bus b) => (int?)b.Id != id && b.PlateNumber.ToUpper() == plate.ToUpper()))
			{
				throw new InvalidOperationException("Biển số đã tồn tại.");
			}
			Bus bus;
			if (id.HasValue)
			{
				int selected = id.GetValueOrDefault();
				bus = appDbContext.Buses.Single((Bus b) => b.Id == selected);
			}
			else
			{
				bus = new Bus();
			}
			Bus bus2 = bus;
			if (id.HasValue && appDbContext.Tickets.Any((Ticket t) => (int?)t.Trip.BusId == id) && (bus2.PlateNumber != plate || bus2.BusModel != text || bus2.BusTypeName != text2 || bus2.TotalSeats != result || bus2.NumberOfFloors != num))
			{
				throw new InvalidOperationException("Xe đã có vé bán: chỉ được thay đổi tiện ích và trạng thái hoạt động.");
			}
			bus2.PlateNumber = plate;
			bus2.BusModel = text;
			bus2.BusTypeName = text2;
			bus2.TotalSeats = result;
			bus2.NumberOfFloors = num;
			bus2.HasWifi = WifiBox.IsChecked == true;
			bus2.HasAirConditioner = AirBox.IsChecked == true;
			bus2.IsActive = BusActiveBox.IsChecked == true;
			if (!id.HasValue)
			{
				appDbContext.Buses.Add(bus2);
			}
			appDbContext.SaveChanges();
			RefreshData();
			BusesGrid.SelectedItem = (BusesGrid.ItemsSource as IEnumerable<Bus>)?.FirstOrDefault((Bus b) => b.Id == bus2.Id);
			Notify("Đã lưu xe.");
		});
	}

	private void BusesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_loading && BusesGrid.SelectedItem is Bus bus)
		{
			PlateBox.Text = bus.PlateNumber;
			BusModelBox.Text = bus.BusModel;
			BusTypeBox.Text = bus.BusTypeName;
			CapacityBox.Text = bus.TotalSeats.ToString(CultureInfo.CurrentCulture);
			FloorsBox.SelectedIndex = bus.NumberOfFloors - 1;
			WifiBox.IsChecked = bus.HasWifi;
			AirBox.IsChecked = bus.HasAirConditioner;
			BusActiveBox.IsChecked = bus.IsActive;
		}
	}

	private void NewBus_Click(object sender, RoutedEventArgs e)
	{
		BusesGrid.SelectedItem = null;
		PlateBox.Clear();
		BusModelBox.Clear();
		BusTypeBox.Text = "Giường nằm";
		CapacityBox.Text = "36";
		FloorsBox.SelectedIndex = 1;
		WifiBox.IsChecked = true;
		AirBox.IsChecked = true;
		BusActiveBox.IsChecked = true;
		PlateBox.Focus();
	}

	private void TripRoute_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (!_loading && TripRouteBox.SelectedItem is BusRoute busRoute)
		{
			TripPriceBox.Text = busRoute.BasePrice.ToString(CultureInfo.CurrentCulture);
			if (PickupBox.Text.Length == 0)
			{
				PickupBox.Text = busRoute.Origin;
			}
			if (DropoffBox.Text.Length == 0)
			{
				DropoffBox.Text = busRoute.Destination;
			}
		}
	}

	private void AddTrip_Click(object sender, RoutedEventArgs e)
	{
		string code;
		Run(() =>
		{
			object? selectedItem = TripRouteBox.SelectedItem;
			BusRoute? route = selectedItem as BusRoute;
			if (route != null)
			{
				selectedItem = TripBusBox.SelectedItem;
				Bus? bus = selectedItem as Bus;
				if (bus != null)
				{
					DateTime departure = EnteredDateTime(DepartureDateBox, DepartureTimeBox, "giờ khởi hành");
					DateTime arrival = EnteredDateTime(ArrivalDateBox, ArrivalTimeBox, "giờ dự kiến đến");
					if (arrival <= departure)
					{
						throw new ArgumentException("Giờ đến phải sau giờ khởi hành.");
					}
					string driverName = Required(DriverBox, "tên tài xế");
					string driverPhone = Required(DriverPhoneBox, "SĐT tài xế");
					decimal num = PositiveMoney(TripPriceBox, "Giá vé");
					int? id = (TripsGrid.SelectedItem as TripRow)?.Trip.Id;
					using AppDbContext appDbContext = new AppDbContext();
					Trip trip;
					if (id.HasValue)
					{
						int selected = id.GetValueOrDefault();
						trip = appDbContext.Trips.Single((Trip t) => t.Id == selected);
					}
					else
					{
						trip = new Trip();
					}
					Trip trip2 = trip;
					bool flag = !id.HasValue || trip2.RouteId != route.Id || trip2.BusId != bus.Id || trip2.DepartureTime != departure || trip2.EstimatedArrivalTime != arrival || trip2.TicketPrice != num;
					bool flag2 = id.HasValue;
					if (flag2)
					{
						TripStatus status = trip2.Status;
						bool flag3 = (uint)status <= 1u;
						flag2 = !flag3;
					}
					if (flag2)
					{
						throw new InvalidOperationException("Chuyến đã xuất bến hoặc đã hủy không thể sửa.");
					}
					if ((id.HasValue && trip2.Status == TripStatus.Boarding) & flag)
					{
						throw new InvalidOperationException("Chuyến đang đón khách: chỉ được sửa tên và SĐT tài xế.");
					}
					if ((id.HasValue & flag) && appDbContext.Tickets.Any((Ticket t) => (int?)t.TripId == id))
					{
						throw new InvalidOperationException("Chuyến đã từng bán vé: chỉ được sửa tên và SĐT tài xế.");
					}
					if (flag)
					{
						if (departure <= DateTime.Now)
						{
							throw new ArgumentException("Giờ khởi hành phải trong tương lai.");
						}
						if (!appDbContext.Routes.Any((BusRoute r) => r.Id == route.Id && r.IsActive) || !appDbContext.Buses.Any((Bus b) => b.Id == bus.Id && b.IsActive))
						{
							throw new InvalidOperationException("Tuyến hoặc xe không còn hoạt động.");
						}
						if (appDbContext.Trips.Any((Trip t) => (int?)t.Id != id && t.BusId == bus.Id && (int)t.Status != 4 && departure < t.EstimatedArrivalTime && arrival > t.DepartureTime))
						{
							throw new InvalidOperationException("Xe đã được phân công chuyến khác trong khoảng thời gian này.");
						}
					}
					if (!id.HasValue)
					{
						string text = $"CX-{departure:yyyyMMdd}-";
						int num2 = 1;
						code = text + num2.ToString("D2", CultureInfo.InvariantCulture);
						while (true)
						{
							if (!appDbContext.Trips.Any((Trip t) => t.TripCode == code))
							{
								break;
							}
							num2++;
							code = text + num2.ToString("D2", CultureInfo.InvariantCulture);
						}
						trip2.TripCode = code;
					}
					trip2.RouteId = route.Id;
					trip2.BusId = bus.Id;
					trip2.DepartureTime = departure;
					trip2.EstimatedArrivalTime = arrival;
					trip2.DriverName = driverName;
					trip2.DriverPhone = driverPhone;
					trip2.TicketPrice = num;
					if (!id.HasValue)
					{
						appDbContext.Trips.Add(trip2);
					}
					appDbContext.SaveChanges();
					RefreshData();
					TripsGrid.SelectedItem = (TripsGrid.ItemsSource as IEnumerable<TripRow>)?.FirstOrDefault((TripRow t) => t.Trip.Id == trip2.Id);
					Notify("Đã lưu chuyến " + trip2.TripCode + ".");
					return;
				}
			}
			throw new ArgumentException("Chọn tuyến và xe.");
		});
	}

	private void TripsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_loading && TripsGrid.SelectedItem is TripRow tripRow)
		{
			Trip trip = tripRow.Trip;
			TripRouteBox.SelectedItem = (TripRouteBox.ItemsSource as IEnumerable<BusRoute>)?.FirstOrDefault((BusRoute r) => r.Id == trip.RouteId);
			TripBusBox.SelectedItem = (TripBusBox.ItemsSource as IEnumerable<Bus>)?.FirstOrDefault((Bus b) => b.Id == trip.BusId);
			DepartureDateBox.SelectedDate = trip.DepartureTime.Date;
			DepartureTimeBox.Text = trip.DepartureTime.ToString("HH:mm", CultureInfo.InvariantCulture);
			ArrivalDateBox.SelectedDate = trip.EstimatedArrivalTime.Date;
			ArrivalTimeBox.Text = trip.EstimatedArrivalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
			DriverBox.Text = trip.DriverName;
			DriverPhoneBox.Text = trip.DriverPhone;
			TripPriceBox.Text = trip.TicketPrice.ToString(CultureInfo.CurrentCulture);
			TripStatusBox.SelectedItem = Statuses.First((StatusChoice s) => s.Value == trip.Status);
		}
	}

	private void NewTrip_Click(object sender, RoutedEventArgs e)
	{
		TripsGrid.SelectedItem = null;
		TripRouteBox.SelectedItem = null;
		TripBusBox.SelectedItem = null;
		DepartureDateBox.SelectedDate = DateTime.Today.AddDays(1.0);
		ArrivalDateBox.SelectedDate = DateTime.Today.AddDays(1.0);
		DepartureTimeBox.Text = "08:00";
		ArrivalTimeBox.Text = "15:00";
		DriverBox.Clear();
		DriverPhoneBox.Clear();
		TripPriceBox.Clear();
		TripRouteBox.Focus();
	}

	private void UpdateTripStatus_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			object? selectedItem = TripsGrid.SelectedItem;
			TripRow? row = selectedItem as TripRow;
			if (row is null || (TripStatusBox.SelectedItem as StatusChoice)?.Value is not { } value)
			{
				throw new ArgumentException("Chọn chuyến và trạng thái cần lưu.");
			}
			using AppDbContext appDbContext = new AppDbContext();
			Trip trip = appDbContext.Trips.Single((Trip t) => t.Id == row.Trip.Id);
			bool flag;
			switch (trip.Status)
			{
			case TripStatus.Scheduled:
			{
				bool flag2 = ((value == TripStatus.Boarding || value == TripStatus.Cancelled) ? true : false);
				flag = flag2;
				break;
			}
			case TripStatus.Boarding:
			{
				bool flag2 = ((value == TripStatus.Departed || value == TripStatus.Cancelled) ? true : false);
				flag = flag2;
				break;
			}
			case TripStatus.Departed:
				flag = value == TripStatus.Completed;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				throw new InvalidOperationException("Không thể chuyển trạng thái chuyến theo hướng này.");
			}
			if (value == TripStatus.Cancelled)
			{
				if (appDbContext.Tickets.Any((Ticket t) => t.TripId == trip.Id && (int)t.Status == 1))
				{
					throw new InvalidOperationException("Không thể hủy chuyến đã có khách soát vé.");
				}
				if (MessageBox.Show(this, "Hủy chuyến sẽ hủy tất cả vé chưa lên xe. Xác nhận?", "Hủy chuyến", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
				{
					return;
				}
				using IDbContextTransaction dbContextTransaction = appDbContext.Database.BeginTransaction();
				BookingService bookingService = new BookingService(appDbContext);
				foreach (string item in (from t in appDbContext.Tickets
					where t.TripId == trip.Id && (int)t.Status == 0
					select t.TicketCode).ToList())
				{
					bookingService.Cancel(item);
				}
				trip.Status = value;
				appDbContext.SaveChanges();
				dbContextTransaction.Commit();
			}
			else
			{
				trip.Status = value;
				appDbContext.SaveChanges();
			}
			RefreshData();
			Notify("Đã cập nhật trạng thái chuyến.");
		});
	}

	private void TripFilter_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (!_loading && _ready)
		{
			FilterTrips();
		}
	}

	private void ClearTripFilter_Click(object sender, RoutedEventArgs e)
	{
		TripFilterDate.SelectedDate = null;
		TripFilterStatus.SelectedIndex = 0;
		FilterTrips();
	}

	private void FilterTrips()
	{
		DateTime? date = TripFilterDate.SelectedDate?.Date;
		TripStatus? status = (TripFilterStatus.SelectedItem as StatusChoice)?.Value;
		TripsGrid.ItemsSource = (from t in _trips.Where((Trip t) =>
			{
				if (date.HasValue)
				{
					DateTime date2 = t.DepartureTime.Date;
					DateTime? dateTime = date;
					if (!(date2 == dateTime))
					{
						return false;
					}
				}
				return !status.HasValue || t.Status == status;
			})
			select new TripRow(t, Statuses.First((StatusChoice s) => s.Value == t.Status).Label)).ToList();
	}

	private void BookingTrip_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (!_loading && _ready)
		{
			UpdateBookingTrip();
		}
	}

	private void UpdateBookingTrip()
	{
		if (BookingTripBox.SelectedItem is not Trip trip)
			return;
		PickupBox.Text = trip.Route.Origin;
		DropoffBox.Text = trip.Route.Destination;
	}

	private void Book_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			if (BookingTripBox.SelectedItem is not Trip trip)
				throw new ArgumentException("Chọn chuyến xe đang nhận khách.");
			string name = Required(CustomerBox, "tên hành khách");
			string phone = Required(CustomerPhoneBox, "số điện thoại");
			string pickup = Required(PickupBox, "điểm đón");
			string dropoff = Required(DropoffBox, "điểm trả");
			string payment = (PaymentBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? throw new ArgumentException("Chọn phương thức thanh toán đã xác nhận.");
			using AppDbContext db = new AppDbContext();
			Ticket ticket = new BookingService(db).Book(trip.Id, name, phone, string.IsNullOrWhiteSpace(CustomerEmailBox.Text) ? null : CustomerEmailBox.Text.Trim(), pickup, dropoff, payment);
			RefreshData();
			CustomerBox.Clear();
			CustomerPhoneBox.Clear();
			CustomerEmailBox.Clear();
			_pdfTicketCode = ticket.TicketCode;
			ExportPdfButton.IsEnabled = true;
			BookingResult.Text = $"Đã bán vé {ticket.TicketCode} | {ticket.CustomerName} | {ticket.Price:N0} ₫";
			Notify("Đã bán vé " + ticket.TicketCode + ". Chọn 'Lưu vé thành PDF' để xuất file.");
		});
	}

	private void ExportPdf_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			if (_pdfTicketCode == null)
			{
				throw new ArgumentException("Chưa có vé để xuất PDF.");
			}
			SaveTicketPdf(_pdfTicketCode);
		});
	}

	private void SaveTicketPdf(string code)
	{
		using AppDbContext appDbContext = new AppDbContext();
		string normalizedCode = code.Trim().ToUpperInvariant();
		Ticket ticket = appDbContext.Tickets.AsNoTracking().Include((Ticket t) => t.Trip).ThenInclude((Trip t) => t.Route)
			.Include((Ticket t) => t.Trip)
			.ThenInclude((Trip t) => t.Bus)
			.SingleOrDefault((Ticket t) => t.TicketCode == normalizedCode) ?? throw new ArgumentException("Không tìm thấy mã vé cần xuất PDF.");
		if (ticket.Status == TicketStatus.Cancelled)
		{
			throw new InvalidOperationException("Không thể xuất vé đã hủy thành PDF.");
		}
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "Lưu vé hành khách",
			Filter = "Tài liệu PDF (*.pdf)|*.pdf",
			DefaultExt = ".pdf",
			AddExtension = true,
			FileName = ticket.TicketCode + ".pdf"
		};
		if (saveFileDialog.ShowDialog(this) == true)
		{
			TicketPdfExporter.Export(ticket, saveFileDialog.FileName);
			Notify("Đã lưu vé PDF: " + saveFileDialog.FileName);
		}
	}

	private void ExportExcel_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			Ticket[] array = (TicketsGrid.ItemsSource as IEnumerable<Ticket>)?.ToArray() ?? Array.Empty<Ticket>();
			if (array.Length == 0)
			{
				throw new InvalidOperationException("Danh sách đang hiển thị không có vé để xuất.");
			}
			SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Title = "Xuất danh sách vé và soát vé",
				Filter = "Sổ Excel (*.xlsx)|*.xlsx",
				DefaultExt = ".xlsx",
				AddExtension = true,
				FileName = $"DanhSachVe_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
			};
			if (saveFileDialog.ShowDialog(this) == true)
			{
				TicketExcelExporter.Export(array, saveFileDialog.FileName);
				Notify($"Đã xuất {array.Length} vé ra Excel: {saveFileDialog.FileName}");
			}
		});
	}

	private void TicketSearch_Changed(object sender, TextChangedEventArgs e)
	{
		if (_ready && !_loading)
		{
			FilterTickets();
		}
	}

	private void FilterTickets()
	{
		string search = TicketSearchBox.Text.Trim();
		TicketsGrid.ItemsSource = _tickets.Where((Ticket t) => search.Length == 0 || t.TicketCode.Contains(search, StringComparison.OrdinalIgnoreCase) || t.CustomerName.Contains(search, StringComparison.OrdinalIgnoreCase) || t.CustomerPhone.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Trip.TripCode.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
	}

	private void TicketsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_loading)
		{
			if (!(TicketsGrid.SelectedItem is Ticket ticket))
			{
				TicketDetail.Text = "";
				MarkRefundedButton.IsEnabled = false;
				return;
			}
			TicketDetail.Text = $"{ticket.TicketCode} | {ticket.CustomerName} - {ticket.CustomerPhone} | {ticket.Trip.Route.RouteName} | {ticket.Trip.DepartureTime:dd/MM/yyyy HH:mm} | {ticket.StatusText} | {ticket.PaymentMethod}";
			BookingResult.Text = TicketDetail.Text;
			_pdfTicketCode = ticket.TicketCode;
			ExportPdfButton.IsEnabled = ticket.Status != TicketStatus.Cancelled;
			MarkRefundedButton.IsEnabled = ticket.Status == TicketStatus.Cancelled && ticket.RefundedAt is null;
		}
	}

	private string SelectedTicketCode()
	{
		if (TicketsGrid.SelectedItem is Ticket ticket)
		{
			return ticket.TicketCode;
		}
		string text = TicketSearchBox.Text.Trim();
		if (text.Length == 0)
		{
			throw new ArgumentException("Chọn vé hoặc nhập chính xác mã vé cần kiểm tra.");
		}
		return text;
	}

	private void ScanQr_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			var scanner = new CameraScanWindow { Owner = this };
			if (scanner.ShowDialog() != true || string.IsNullOrWhiteSpace(scanner.ScannedCode))
				return;
			var code = scanner.ScannedCode.Trim().ToUpperInvariant();
			TicketSearchBox.Text = code;
			TicketsGrid.SelectedItem = (TicketsGrid.ItemsSource as IEnumerable<Ticket>)?
				.FirstOrDefault(ticket => ticket.TicketCode == code);
			using var db = new AppDbContext();
			var result = new BookingService(db).Validate(code);
			TicketDetail.Text = $"{result.Message} | Có thể soát vé: {(result.CanCheckIn ? "Có" : "Không")}";
			Notify(result.Message);
		});
	}

	private void CheckIn_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			using AppDbContext db = new AppDbContext();
			Ticket ticket = new BookingService(db).CheckIn(SelectedTicketCode());
			RefreshData();
			Notify($"Đã soát vé {ticket.TicketCode}.");
		});
	}

	private void CancelTicket_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			var code = SelectedTicketCode();
			var choice = new RefundChoiceWindow(code) { Owner = this };
			if (choice.ShowDialog() != true)
				return;
			using var db = new AppDbContext();
			var ticket = new BookingService(db).Cancel(code, choice.Refunded);
			RefreshData();
			Notify($"Đã hủy vé {ticket.TicketCode}; {ticket.StatusText}. Chuyến có thể nhận thêm một hành khách.");
		});
	}

	private void MarkRefunded_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			var code = SelectedTicketCode();
			if (MessageBox.Show(this, $"Chỉ xác nhận đã hoàn tiền cho vé {code} nếu tiền đã thực trả cho khách.",
				    "Xác nhận hoàn tiền", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
				return;
			using var db = new AppDbContext();
			var ticket = new BookingService(db).MarkRefunded(code);
			RefreshData();
			Notify($"Vé {ticket.TicketCode}: đã ghi nhận hoàn tiền.");
		});
	}

	private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_ready && !_loading && ReferenceEquals(e.OriginalSource, MainTabs) && MainTabs.SelectedIndex == 6)
			Run(RefreshRevenueReport);
	}

	private void ReportApply_Click(object sender, RoutedEventArgs e) => Run(RefreshRevenueReport);

	private void ReportReset_Click(object sender, RoutedEventArgs e)
	{
		ReportFromDate.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
		ReportToDate.SelectedDate = DateTime.Today;
		ReportRouteBox.SelectedIndex = 0;
		Run(RefreshRevenueReport);
	}

	private void ReportExportExcel_Click(object sender, RoutedEventArgs e)
	{
		Run(() =>
		{
			var report = LoadRevenueReport();
			DisplayRevenueReport(report);
			var route = (ReportRouteBox.SelectedItem as ReportRouteChoice)?.Label ?? "Tất cả tuyến";
			var dialog = new SaveFileDialog
			{
				Title = "Xuất báo cáo doanh thu",
				Filter = "Sổ Excel (*.xlsx)|*.xlsx",
				DefaultExt = ".xlsx",
				AddExtension = true,
				FileName = $"DoanhThu_{report.From:yyyyMMdd}_{report.To:yyyyMMdd}.xlsx"
			};
			if (dialog.ShowDialog(this) == true)
			{
				RevenueExcelExporter.Export(report, route, dialog.FileName);
				Notify("Đã xuất báo cáo doanh thu ra Excel: " + dialog.FileName);
			}
		});
	}

	private static string ReportMoney(decimal amount) =>
		amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";

	private static string ChartMoney(decimal amount)
	{
		if (amount >= 1_000_000_000m)
			return $"{amount / 1_000_000_000m:0.#} tỷ";
		if (amount >= 1_000_000m)
			return $"{amount / 1_000_000m:0.#} tr";
		if (amount >= 1_000m)
			return $"{amount / 1_000m:0.#} nghìn";
		return amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
	}

	private RevenueReport LoadRevenueReport()
	{
		if (!_ready)
			throw new InvalidOperationException("Dữ liệu chưa được tải.");
		if (ReportFromDate.SelectedDate is not { } from || ReportToDate.SelectedDate is not { } to)
			throw new ArgumentException("Chọn ngày bắt đầu và ngày kết thúc báo cáo.");

		using var db = new AppDbContext();
		return new RevenueReportService(db).Generate(from, to,
			(ReportRouteBox.SelectedItem as ReportRouteChoice)?.Id);
	}

	private void RefreshRevenueReport()
	{
		if (_ready)
			DisplayRevenueReport(LoadRevenueReport());
	}

	private void DisplayRevenueReport(RevenueReport report)
	{
		ReportPeriodText.Text = $"Ngày bán vé: {report.From:dd/MM/yyyy} – {report.To:dd/MM/yyyy} · {report.TrendGranularity}. " +
			"Số liệu theo trạng thái vé hiện tại; không phải đối soát ngân hàng.";

		ReportRevenueValue.Text = ReportMoney(report.Revenue);
		ReportActiveValue.Text = report.ActiveCount.ToString("N0");
		ReportCheckedInValue.Text = report.CheckedInCount.ToString("N0");
		ReportCancelledValue.Text = report.CancelledCount.ToString("N0");
		ReportCancelledAmountValue.Text = ReportMoney(report.CancelledValue);
		ReportRefundedValue.Text = report.RefundedCount.ToString("N0");
		ReportPendingRefundValue.Text = report.PendingRefundCount.ToString("N0");
		ReportTicketCountValue.Text = report.TicketCount.ToString("N0");
		ReportRefundedAmountValue.Text = ReportMoney(report.RefundedValue);
		ReportPendingRefundAmountValue.Text = ReportMoney(report.PendingRefundValue);

		decimal trendMax = report.Trend.Count == 0 ? 0 : report.Trend.Max(point => point.Revenue);
		ReportTrendChart.ItemsSource = report.Trend.Select(point => new TrendBar(
			point.Label, ChartMoney(point.Revenue),
			trendMax == 0 ? 0 : (double)(point.Revenue / trendMax) * 122,
			$"{point.Label}: {ReportMoney(point.Revenue)} · {point.ActiveTickets} vé hợp lệ · {point.CancelledTickets} vé hủy")).ToList();
		ReportTrendEmpty.Visibility = report.TicketCount == 0 ? Visibility.Visible : Visibility.Collapsed;

		decimal routeMax = report.Routes.Count == 0 ? 0 : report.Routes.Max(route => route.Revenue);
		ReportRouteChart.ItemsSource = report.Routes.Select(route => new BreakdownBar(
			route.RouteName, ChartMoney(route.Revenue),
			routeMax == 0 ? 0 : (double)(route.Revenue / routeMax) * 240,
			$"{route.RouteCode} · {route.RouteName}: {ReportMoney(route.Revenue)} · {route.ActiveTickets} vé hợp lệ · {route.CancelledTickets} vé hủy")).ToList();
		ReportRouteEmpty.Visibility = report.Routes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

		decimal paymentMax = report.Payments.Count == 0 ? 0 : report.Payments.Max(method => method.Revenue);
		ReportPaymentChart.ItemsSource = report.Payments.Select(method => new BreakdownBar(
			method.PaymentMethod, ChartMoney(method.Revenue),
			paymentMax == 0 ? 0 : (double)(method.Revenue / paymentMax) * 240,
			$"{method.PaymentMethod}: {ReportMoney(method.Revenue)} · {method.ActiveTickets} vé hợp lệ")).ToList();
		ReportPaymentEmpty.Visibility = report.Payments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

		ReportTripsGrid.ItemsSource = report.Trips;
		ReportTicketsGrid.ItemsSource = report.Tickets;
		Notify("Đã cập nhật báo cáo doanh thu.");
	}

}
