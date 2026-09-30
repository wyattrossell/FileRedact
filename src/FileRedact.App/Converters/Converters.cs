using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FileRedact.Core.Model;

namespace FileRedact.App.Converters;

/// <summary>Stroke colour per PII category so reviewers can tell a name from an SSN at a glance.</summary>
public sealed class CategoryToBrushConverter : IValueConverter
{
    private static readonly Dictionary<PiiCategory, Brush> Brushes = new()
    {
        [PiiCategory.Name] = Make("#2563EB"),
        [PiiCategory.DateOfBirth] = Make("#7C3AED"),
        [PiiCategory.Date] = Make("#A78BFA"),
        [PiiCategory.SocialSecurityNumber] = Make("#DC2626"),
        [PiiCategory.DriverLicense] = Make("#EA580C"),
        [PiiCategory.PassportNumber] = Make("#EA580C"),
        [PiiCategory.Address] = Make("#059669"),
        [PiiCategory.PhoneNumber] = Make("#0891B2"),
        [PiiCategory.EmailAddress] = Make("#0891B2"),
        [PiiCategory.FinancialAccount] = Make("#B91C1C"),
        [PiiCategory.VehicleIdentifier] = Make("#65A30D"),
        [PiiCategory.CriminalJusticeIdentifier] = Make("#9F1239"),
        [PiiCategory.PlaceOfBirth] = Make("#7C3AED"),
        [PiiCategory.MaidenName] = Make("#2563EB"),
        [PiiCategory.IpAddress] = Make("#6B7280"),
        [PiiCategory.OnlineHandle] = Make("#0D9488"),
        [PiiCategory.WebAddress] = Make("#4B5563"),
        [PiiCategory.CustomTerm] = Make("#D97706"),
        [PiiCategory.Manual] = Make("#111827"),
    };

    private static Brush Make(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is PiiCategory c && Brushes.TryGetValue(c, out var b) ? b : System.Windows.Media.Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value == null || (value is string s && s.Length == 0);
        return (isNull ^ Invert) ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        return (b ^ Invert) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
