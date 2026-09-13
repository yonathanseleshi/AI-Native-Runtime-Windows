using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AI_Native_Runtime_Windows.Shared
{
    /// <summary>WinUI 3 (unlike UWP) ships no built-in bool-to-Visibility converter -
    /// one shared instance here, used by every surface's loading/empty/error/data
    /// state pattern (`desktop-shell-conventions.md` §6), rather than one per page.</summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var b = value is bool v && v;
            if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase)) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
