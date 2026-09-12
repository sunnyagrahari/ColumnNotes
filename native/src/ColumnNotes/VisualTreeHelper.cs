using System.Windows;
using System.Windows.Media;

namespace ColumnNotes;

/// <summary>
/// Same-namespace stand-in so MainWindow's VisualTreeHelper.GetParent
/// calls are safe on Paragraph/Run (not Visuals).
/// </summary>
static class VisualTreeHelper
{
    public static DependencyObject? GetParent(DependencyObject? obj)
    {
        if (obj is null) return null;
        if (obj is not Visual)
            return LogicalTreeHelper.GetParent(obj);
        try { return System.Windows.Media.VisualTreeHelper.GetParent(obj); }
        catch (InvalidOperationException) { return LogicalTreeHelper.GetParent(obj); }
    }
}
