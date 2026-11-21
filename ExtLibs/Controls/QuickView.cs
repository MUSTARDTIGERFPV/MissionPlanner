using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SkiaSharp.Views.Desktop;

namespace MissionPlanner.Controls
{
    public partial class QuickView : SkiaSharp.Views.Desktop.SKControl
    {
        [System.ComponentModel.Browsable(true)]
        public string desc
        {
            get { return _desc; } set { if (_desc == value) return; _desc = value; Invalidate(); }
        }

        double _number = -9999;

        [System.ComponentModel.Browsable(true)]
        public double number
        {
            get { return _number; }
            set
            {
                lock (this)
                {
                    if (_number.Equals(value))
                        return;
                    _number = value;
                    // only repaint when the displayed text changes
                    var text = value.ToString(_numberformat);
                    if (text == _numberText)
                        return;
                    _numberText = text;
                    Invalidate();
                }
            }
        }

        string _numberformat = "0.00";
        string _numberText;
        private string _desc = "";
        private Color _numbercolor;

        [System.ComponentModel.Browsable(true)]
        public string numberformat
        {
            get
            {
                return _numberformat;
            }
            set
            {
                if (_numberformat.Equals(value))
                    return;
                _numberformat = value;
                _numberText = null;
                this.Invalidate();
            }
        }

        [System.ComponentModel.Browsable(true)]
        public Color numberColor { get { return _numbercolor; } set { if (_numbercolor == value) return; _numbercolor = value; Invalidate(); } }

        //We use this property as a backup store for the numberColor, so it is possible to change numberColor temporary.
        public Color numberColorBackup { get; set; }

        public QuickView()
        {
            InitializeComponent();

            PaintSurface+= OnPaintSurface;
            Disposed += (s, e) => ReleasePaintCache();
        }

        // paint resources, recreated only when the size/font/colour they were made from changes
        Font _numberFont;
        float _numberFontSize;
        Font _numberFontBase;
        SolidBrush _descBrush;
        SolidBrush _numberBrush;

        Font GetNumberFont(float size)
        {
            if (_numberFont == null || _numberFontSize != size || !ReferenceEquals(_numberFontBase, this.Font))
            {
                _numberFont?.Dispose();
                _numberFont = new Font(this.Font.FontFamily, size, this.Font.Style);
                _numberFontSize = size;
                _numberFontBase = this.Font;
            }

            return _numberFont;
        }

        static SolidBrush GetBrush(ref SolidBrush brush, Color color)
        {
            if (brush == null || brush.Color != color)
            {
                brush?.Dispose();
                brush = new SolidBrush(color);
            }

            return brush;
        }

        void ReleasePaintCache()
        {
            _numberFont?.Dispose();
            _numberFont = null;
            _descBrush?.Dispose();
            _descBrush = null;
            _numberBrush?.Dispose();
            _numberBrush = null;
        }

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e2)
        {
            var e = new SkiaGraphics(e2.Surface);
            e2.Surface.Canvas.Clear();
            int y = 0;
            {
                Size extent = e.MeasureString(desc, this.Font).ToSize();

                var mid = extent.Width / 2;

                e.DrawString(desc, this.Font, GetBrush(ref _descBrush, this.ForeColor), this.Width / 2 - mid, 5);

                y = extent.Height;
            }
            //
            {
                var numb = number.ToString(numberformat);

                Size extent = e.MeasureString("0".PadLeft(numb.Length+1,'0'), GetNumberFont((float)newSize)).ToSize();

                float hRatio = (this.Height - y) / (float)(extent.Height);
                float wRatio = this.Width / (float)extent.Width;
                float ratio = (hRatio < wRatio) ? hRatio : wRatio;

                newSize = (newSize * ratio);// * 0.75f; // pixel to points

                newSize -= newSize % 5;

                if (newSize < 8 || newSize > 999999)
                    newSize = 8;

                var numberFont = GetNumberFont((float)newSize);

                extent = e.MeasureString(numb, numberFont).ToSize();

                e.DrawString(numb, numberFont, GetBrush(ref _numberBrush, this.numberColor), this.Width / 2 - extent.Width / 2, y + ((this.Height - y) / 2 - extent.Height / 2));
            }
        }

        public override void Refresh()
        {
            if (this.Visible)
                base.Refresh();
        }

        protected override void WndProc(ref Message m) // seems to crash here on linux... so try ignore it
        {
            try
            {
                base.WndProc(ref m);
            }
            catch { }
        }

        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            if (this.Visible && this.ThisReallyVisible())
                base.OnInvalidated(e);
        }

        float newSize = 8;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
        }
    }
}
