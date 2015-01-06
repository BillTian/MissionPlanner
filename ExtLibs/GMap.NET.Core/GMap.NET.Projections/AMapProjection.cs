<<<<<<< HEAD
﻿
namespace GMap.NET.Projections
{
    using System;

    /// <summary>
    /// The Mercator projection
    /// PROJCS["World_Mercator",GEOGCS["GCS_WGS_1984",DATUM["D_WGS_1984",SPHEROID["WGS_1984",6378137,298.257223563]],PRIMEM["Greenwich",0],UNIT["Degree",0.017453292519943295]],PROJECTION["Mercator"],PARAMETER["False_Easting",0],PARAMETER["False_Northing",0],PARAMETER["Central_Meridian",0],PARAMETER["standard_parallel_1",0],UNIT["Meter",1]]
    /// </summary>
    public class AMapProjection : PureProjection
    {
        public static readonly AMapProjection Instance = new AMapProjection();

        static readonly double MinLatitude = -85.05112878;
        static readonly double MaxLatitude = 85.05112878;
        static readonly double MinLongitude = -180;
        static readonly double MaxLongitude = 180;

        internal double a = 6378245.0;
        internal double ee = 0.00669342162296594323;

        public override RectLatLng Bounds
        {
            get
            {
                return RectLatLng.FromLTRB(MinLongitude, MaxLatitude, MaxLongitude, MinLatitude);
            }
        }

        readonly GSize tileSize = new GSize(256, 256);
        public override GSize TileSize
        {
            get
            {
                return tileSize;
            }
        }

        public override double Axis
        {
            get
            {
                return 6378137;
            }
        }

        public override double Flattening
        {
            get
            {
                return (1.0 / 298.257223563);
            }
        }

        public override GPoint FromLatLngToPixel(double lat, double lng, int zoom)
        {
            double[] d = new double[2];
            transform(lat, lng, d);
            lat = d[0];
            lng = d[1];


            GPoint ret = GPoint.Empty;

            lat = Clip(lat, MinLatitude, MaxLatitude);
            lng = Clip(lng, MinLongitude, MaxLongitude);

            double x = (lng + 180) / 360;
            double sinLatitude = Math.Sin(lat * Math.PI / 180);
            double y = 0.5 - Math.Log((1 + sinLatitude) / (1 - sinLatitude)) / (4 * Math.PI);

            GSize s = GetTileMatrixSizePixel(zoom);
            long mapSizeX = s.Width;
            long mapSizeY = s.Height;

            ret.X = (long)Clip(x * mapSizeX + 0.5, 0, mapSizeX - 1);
            ret.Y = (long)Clip(y * mapSizeY + 0.5, 0, mapSizeY - 1);

            return ret;
        }

        public override PointLatLng FromPixelToLatLng(long x, long y, int zoom)
        {
            PointLatLng ret = PointLatLng.Empty;

            GSize s = GetTileMatrixSizePixel(zoom);
            double mapSizeX = s.Width;
            double mapSizeY = s.Height;

            double xx = (Clip(x, 0, mapSizeX - 1) / mapSizeX) - 0.5;
            double yy = 0.5 - (Clip(y, 0, mapSizeY - 1) / mapSizeY);

            ret.Lat = 90 - 360 * Math.Atan(Math.Exp(-yy * 2 * Math.PI)) / Math.PI;
            ret.Lng = 360 * xx;

            PointLatLng p = new PointLatLng();
            double[] d = new double[2];
            transform(ret.Lat, ret.Lng, d);
            p.Lat = d[0];
            p.Lng = d[1];

            ret.Lat = ret.Lat - (p.Lat - ret.Lat);
            ret.Lng = ret.Lng - (p.Lng - ret.Lng);

            return ret;
        }

        public override GSize GetTileMatrixMinXY(int zoom)
        {
            return new GSize(0, 0);
        }

        public override GSize GetTileMatrixMaxXY(int zoom)
        {
            long xy = (1 << zoom);
            return new GSize(xy - 1, xy - 1);
        }

        void transform(double wgLat, double wgLon, double[] latlng)
        {
            if (outOfChina(wgLat, wgLon))
            {
                latlng[0] = wgLat;
                latlng[1] = wgLon;
                return;
            }
            double dLat = transformLat(wgLon - 105.0, wgLat - 35.0);
            double dLon = transformLon(wgLon - 105.0, wgLat - 35.0);
            double radLat = wgLat / 180.0 * System.Math.PI;
            double magic = System.Math.Sin(radLat);
            magic = 1 - ee * magic * magic;
            double sqrtMagic = System.Math.Sqrt(magic);
            dLat = (dLat * 180.0) / ((a * (1 - ee)) / (magic * sqrtMagic) * System.Math.PI);
            dLon = (dLon * 180.0) / (a / sqrtMagic * System.Math.Cos(radLat) * System.Math.PI);
            latlng[0] = wgLat + dLat;
            latlng[1] = wgLon + dLon;
        }

        static bool outOfChina(double lat, double lon)
        {
            if (lon < 72.004 || lon > 137.8347)
                return true;
            if (lat < 0.8293 || lat > 55.8271)
                return true;
            return false;
        }

        private double transformLat(double x, double y)
        {
            double ret = -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * System.Math.Sqrt(System.Math.Abs(x));
            ret += (20.0 * System.Math.Sin(6.0 * x * System.Math.PI) + 20.0 * System.Math.Sin(2.0 * x * System.Math.PI)) * 2.0 / 3.0;
            ret += (20.0 * System.Math.Sin(y * System.Math.PI) + 40.0 * System.Math.Sin(y / 3.0 * System.Math.PI)) * 2.0 / 3.0;
            ret += (160.0 * System.Math.Sin(y / 12.0 * System.Math.PI) + 320 * System.Math.Sin(y * System.Math.PI / 30.0)) * 2.0 / 3.0;
            return ret;
        }

        private double transformLon(double x, double y)
        {
            double ret = 300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * System.Math.Sqrt(System.Math.Abs(x));
            ret += (20.0 * System.Math.Sin(6.0 * x * System.Math.PI) + 20.0 * System.Math.Sin(2.0 * x * System.Math.PI)) * 2.0 / 3.0;
            ret += (20.0 * System.Math.Sin(x * System.Math.PI) + 40.0 * System.Math.Sin(x / 3.0 * System.Math.PI)) * 2.0 / 3.0;
            ret += (150.0 * System.Math.Sin(x / 12.0 * System.Math.PI) + 300.0 * System.Math.Sin(x / 30.0 * System.Math.PI)) * 2.0 / 3.0;
            return ret;
        }
    }
}
=======
﻿namespace GMap.NET.Projections
{
    using GMap.NET;
    using System;

    public class AMapProjection : PureProjection
    {
        internal double a = 6378245.0;
        internal double ee = 0.0066934216229659433;
        public static readonly AMapProjection Instance = new AMapProjection();
        private static readonly double MaxLatitude = 85.05112878;
        private static readonly double MaxLongitude = 180.0;
        private static readonly double MinLatitude = -85.05112878;
        private static readonly double MinLongitude = -180.0;
        private readonly GSize tileSize = new GSize(0x100L, 0x100L);

        public override GPoint FromLatLngToPixel(double lat, double lng, int zoom)
        {
            double[] latlng = new double[2];
            this.transform(lat, lng, latlng);
            lat = latlng[0];
            lng = latlng[1];
            GPoint empty = GPoint.Empty;
            lat = PureProjection.Clip(lat, MinLatitude, MaxLatitude);
            lng = PureProjection.Clip(lng, MinLongitude, MaxLongitude);
            double num = (lng + 180.0) / 360.0;
            double num2 = Math.Sin((lat * 3.1415926535897931) / 180.0);
            double num3 = 0.5 - (Math.Log((1.0 + num2) / (1.0 - num2)) / 12.566370614359172);
            GSize tileMatrixSizePixel = this.GetTileMatrixSizePixel(zoom);
            long width = tileMatrixSizePixel.Width;
            long height = tileMatrixSizePixel.Height;
            empty.X = (long)PureProjection.Clip((num * width) + 0.5, 0.0, (double)(width - 1L));
            empty.Y = (long)PureProjection.Clip((num3 * height) + 0.5, 0.0, (double)(height - 1L));
            return empty;
        }

        public override PointLatLng FromPixelToLatLng(long x, long y, int zoom)
        {
            PointLatLng empty = PointLatLng.Empty;
            GSize tileMatrixSizePixel = this.GetTileMatrixSizePixel(zoom);
            double width = tileMatrixSizePixel.Width;
            double height = tileMatrixSizePixel.Height;
            double num3 = (PureProjection.Clip((double)x, 0.0, width - 1.0) / width) - 0.5;
            double num4 = 0.5 - (PureProjection.Clip((double)y, 0.0, height - 1.0) / height);
            empty.Lat = 90.0 - ((360.0 * Math.Atan(Math.Exp((-num4 * 2.0) * 3.1415926535897931))) / 3.1415926535897931);
            empty.Lng = 360.0 * num3;
            PointLatLng lng2 = new PointLatLng();
            double[] latlng = new double[2];
            this.transform(empty.Lat, empty.Lng, latlng);
            lng2.Lat = latlng[0];
            lng2.Lng = latlng[1];
            empty.Lat -= lng2.Lat - empty.Lat;
            empty.Lng -= lng2.Lng - empty.Lng;
            return empty;
        }

        public override GSize GetTileMatrixMaxXY(int zoom)
        {
            long num = ((int)1) << zoom;
            return new GSize(num - 1L, num - 1L);
        }

        public override GSize GetTileMatrixMinXY(int zoom)
        {
            return new GSize(0L, 0L);
        }

        private static bool outOfChina(double lat, double lon)
        {
            if (((lon >= 72.004) && (lon <= 137.8347)) && ((lat >= 0.8293) && (lat <= 55.8271)))
            {
                return false;
            }
            return true;
        }

        private void transform(double wgLat, double wgLon, double[] latlng)
        {
            if (outOfChina(wgLat, wgLon))
            {
                latlng[0] = wgLat;
                latlng[1] = wgLon;
            }
            else
            {
                double num = this.transformLat(wgLon - 105.0, wgLat - 35.0);
                double num2 = this.transformLon(wgLon - 105.0, wgLat - 35.0);
                double a = (wgLat / 180.0) * 3.1415926535897931;
                double d = Math.Sin(a);
                d = 1.0 - ((this.ee * d) * d);
                double num5 = Math.Sqrt(d);
                num = (num * 180.0) / (((this.a * (1.0 - this.ee)) / (d * num5)) * 3.1415926535897931);
                num2 = (num2 * 180.0) / (((this.a / num5) * Math.Cos(a)) * 3.1415926535897931);
                latlng[0] = wgLat + num;
                latlng[1] = wgLon + num2;
            }
        }

        private double transformLat(double x, double y)
        {
            double num = ((((-100.0 + (2.0 * x)) + (3.0 * y)) + ((0.2 * y) * y)) + ((0.1 * x) * y)) + (0.2 * Math.Sqrt(Math.Abs(x)));
            num += (((20.0 * Math.Sin((6.0 * x) * 3.1415926535897931)) + (20.0 * Math.Sin((2.0 * x) * 3.1415926535897931))) * 2.0) / 3.0;
            num += (((20.0 * Math.Sin(y * 3.1415926535897931)) + (40.0 * Math.Sin((y / 3.0) * 3.1415926535897931))) * 2.0) / 3.0;
            return (num + ((((160.0 * Math.Sin((y / 12.0) * 3.1415926535897931)) + (320.0 * Math.Sin((y * 3.1415926535897931) / 30.0))) * 2.0) / 3.0));
        }

        private double transformLon(double x, double y)
        {
            double num = ((((300.0 + x) + (2.0 * y)) + ((0.1 * x) * x)) + ((0.1 * x) * y)) + (0.1 * Math.Sqrt(Math.Abs(x)));
            num += (((20.0 * Math.Sin((6.0 * x) * 3.1415926535897931)) + (20.0 * Math.Sin((2.0 * x) * 3.1415926535897931))) * 2.0) / 3.0;
            num += (((20.0 * Math.Sin(x * 3.1415926535897931)) + (40.0 * Math.Sin((x / 3.0) * 3.1415926535897931))) * 2.0) / 3.0;
            return (num + ((((150.0 * Math.Sin((x / 12.0) * 3.1415926535897931)) + (300.0 * Math.Sin((x / 30.0) * 3.1415926535897931))) * 2.0) / 3.0));
        }

        public override double Axis
        {
            get
            {
                return 6378137.0;
            }
        }

        public override RectLatLng Bounds
        {
            get
            {
                return RectLatLng.FromLTRB(MinLongitude, MaxLatitude, MaxLongitude, MinLatitude);
            }
        }

        public override double Flattening
        {
            get
            {
                return 0.0033528106647474805;
            }
        }

        public override GSize TileSize
        {
            get
            {
                return this.tileSize;
            }
        }
    }
}

>>>>>>> diy/master
