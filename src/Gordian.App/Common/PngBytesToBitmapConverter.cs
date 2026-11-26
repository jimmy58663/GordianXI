// src/Gordian.App/Common/PngBytesToBitmapConverter.cs
using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Gordian.App.Common
{
    /// <summary>Shows PNG bytes from a view model (a QR code, say) as an image; null or unreadable bytes show nothing.</summary>
    public sealed class PngBytesToBitmapConverter : IValueConverter
    {
        public static readonly PngBytesToBitmapConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not byte[] { Length: > 0 } png) return null;
            try
            {
                using var stream = new MemoryStream(png);
                return new Bitmap(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
