using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HeThongQuanLyVeXeBus;

/// <summary>Records the operator's real-world refund action; it does not transfer money.</summary>
public sealed class RefundChoiceWindow : Window
{
    private readonly RadioButton _pending = new() { Content = "Chưa hoàn tiền — theo dõi để xử lý sau", IsChecked = true };
    private readonly RadioButton _refunded = new() { Content = "Đã hoàn tiền — tiền đã thực trả cho khách" };

    public bool Refunded => _refunded.IsChecked == true;

    public RefundChoiceWindow(string ticketCode)
    {
        Title = "Hủy vé và tình trạng hoàn tiền";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = $"Hủy vé {ticketCode}?",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Chọn tình trạng hoàn tiền thực tế. Hủy vé giải phóng ghế nhưng không tự chuyển tiền.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        });
        _pending.Margin = new Thickness(0, 0, 0, 14);
        _refunded.Margin = new Thickness(0, 0, 0, 20);
        content.Children.Add(_pending);
        content.Children.Add(_refunded);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var back = new Button { Content = "Quay lại", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 10, 0) };
        back.Click += (_, _) => DialogResult = false;
        var confirm = new Button
        {
            Content = "Xác nhận hủy vé",
            Padding = new Thickness(14, 7, 14, 7),
            Background = new SolidColorBrush(Color.FromRgb(8, 127, 120)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        confirm.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(back);
        buttons.Children.Add(confirm);
        content.Children.Add(buttons);
        Content = content;
    }
}
