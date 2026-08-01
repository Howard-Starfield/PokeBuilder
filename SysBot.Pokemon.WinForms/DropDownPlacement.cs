using System;
using System.Drawing;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms;

internal static class DropDownPlacement
{
    public static void ShowBelow(
        Control owner,
        ToolStripDropDown dropDown,
        int minimumWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(dropDown);

        var workingArea = Screen.FromControl(owner).WorkingArea;
        var preferred = dropDown.GetPreferredSize(workingArea.Size);
        var width = Math.Clamp(
            Math.Max(minimumWidth, preferred.Width),
            1,
            workingArea.Width);

        dropDown.MinimumSize = new Size(width, 0);
        dropDown.MaximumSize = workingArea.Size;
        preferred = dropDown.GetPreferredSize(new Size(width, workingArea.Height));
        var height = Math.Clamp(preferred.Height, 1, workingArea.Height);

        var below = owner.PointToScreen(new Point(0, owner.Height));
        var ownerTop = owner.PointToScreen(Point.Empty).Y;
        var x = Math.Clamp(
            below.X,
            workingArea.Left,
            workingArea.Right - width);
        var y = below.Y + height <= workingArea.Bottom
            ? below.Y
            : ownerTop - height >= workingArea.Top
                ? ownerTop - height
                : workingArea.Top;

        dropDown.Show(new Point(x, y));
    }
}
