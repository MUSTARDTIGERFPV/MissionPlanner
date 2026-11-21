using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;

namespace MissionPlanner.Maps
{
    [Serializable]
    public class GMapMarkerPOI : GMarkerGoogle
    {
        public GMapMarkerPOI(PointLatLng p)
            : base(p, GMarkerGoogleType.red_dot)
        {
        }

        // draws only the GMarkerGoogle bitmap
        public override bool CanCullOffscreen
        {
            get { return GetType() == typeof(GMapMarkerPOI); }
        }
    }
}