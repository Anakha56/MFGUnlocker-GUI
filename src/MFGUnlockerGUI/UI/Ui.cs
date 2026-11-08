using System.Drawing;
using System.Windows.Forms;

namespace MFGUnlockerGUI.UI
{
    /// <summary>Small layout helpers so every window scales the same way on high-DPI screens.</summary>
    internal static class Ui
    {
        /// <summary>Scale by the screen's DPI (design size is 96 DPI), not by font size.</summary>
        public static void Dpi(Form f)
        {
            f.AutoScaleDimensions = new SizeF(96F, 96F);
            f.AutoScaleMode = AutoScaleMode.Dpi;
        }

        /// <summary>A button that grows with its text and the screen scale, never smaller than minWidth (in 96-DPI pixels).</summary>
        public static Button Btn(string text, DialogResult result = DialogResult.None, int minWidth = 80)
        {
            return new Button
            {
                Text = text, DialogResult = result, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(minWidth, 0), Margin = new Padding(3, 3, 3, 3)
            };
        }

        /// <summary>A right-aligned row of buttons that sizes itself to the buttons.</summary>
        public static FlowLayoutPanel ButtonRow(params Button[] buttons)
        {
            var p = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0)
            };
            p.Controls.AddRange(buttons);
            return p;
        }

        /// <summary>A one-column table that fills the form; add controls with Rows (AutoSize unless stated).</summary>
        public static TableLayoutPanel Column(int padding = 16)
        {
            return new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(padding),
                AutoSize = false
            };
        }

        public static void AddAuto(TableLayoutPanel t, Control c)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c, 0, t.RowCount++);
        }

        public static void AddFill(TableLayoutPanel t, Control c)
        {
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(c, 0, t.RowCount++);
        }
    }
}
