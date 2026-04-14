using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace OpenPatro.Infrastructure;

public sealed class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string urlString && !string.IsNullOrWhiteSpace(urlString))
        {
            try
            {
                return new BitmapImage(new Uri(urlString));
            }
            catch
            {
                // Return null if URL is invalid
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
