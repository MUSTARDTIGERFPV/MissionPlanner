using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace MissionPlanner.Maps
{
    [Serializable]
    public class GMapMarkerBase: GMapMarker
    {
        public static bool DisplayCOGSetting = true;
        public static bool DisplayHeadingSetting = true;
        public static bool DisplayNavBearingSetting = true;
        public static bool DisplayRadiusSetting = true;
        public static bool DisplayTargetSetting = true;
        public static int length = 500;
        public static InactiveDisplayStyleEnum InactiveDisplayStyle = InactiveDisplayStyleEnum.Normal;

        // shared drawing objects for the vehicle markers; markers only paint on the UI thread.
        // never modify these, use a copy
        internal static readonly Pen HeadingPen = new Pen(Color.Red, 2);
        internal static readonly Pen NavBearingPen = new Pen(Color.Green, 2);
        internal static readonly Pen CogPen = new Pen(Color.Black, 2);
        internal static readonly Pen TargetPen = new Pen(Color.Orange, 2);
        internal static readonly Pen RadiusPen = new Pen(Color.HotPink, 2);
        internal static readonly Font SysidFont = new Font(FontFamily.GenericMonospace, 15, FontStyle.Bold);
        
        // Instance variables
        public bool IsActive = true;
        protected bool IsHidden => InactiveDisplayStyle == InactiveDisplayStyleEnum.Hidden && !IsActive;
        protected bool IsTransparent => InactiveDisplayStyle == InactiveDisplayStyleEnum.Transparent && !IsActive;
        protected bool DisplayCOG => DisplayCOGSetting && !IsTransparent;
        protected bool DisplayHeading => DisplayHeadingSetting && !IsTransparent;
        protected bool DisplayNavBearing => DisplayNavBearingSetting && !IsTransparent;
        protected bool DisplayRadius => DisplayRadiusSetting && !IsTransparent;
        protected bool DisplayTarget => DisplayTargetSetting && !IsTransparent;
        
        public GMapMarkerBase(PointLatLng pos):base(pos)
        {
        }

        public enum InactiveDisplayStyleEnum
        {
            Normal,
            Transparent,
            Hidden
        }
    }
}
