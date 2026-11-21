using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using GMap.NET.WindowsForms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Maps.Tests
{
    [TestClass]
    public class GMapImageProxyTests
    {
        static byte[] Encode(ImageFormat format, bool alpha)
        {
            using (var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                    bmp.SetPixel(x, y, Color.FromArgb(alpha ? (x + y) / 2 : 255, x, y, (x * y) & 255));

                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, format);
                    return ms.ToArray();
                }
            }
        }

        static Bitmap ToArgb(Image img)
        {
            var ret = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(ret))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height), 0, 0, img.Width, img.Height,
                    GraphicsUnit.Pixel);
            }

            return ret;
        }

        static void AssertSamePixels(Image expected, Image actual, int tolerance)
        {
            using (var e = ToArgb(expected))
            using (var a = ToArgb(actual))
            {
                for (int y = 0; y < e.Height; y += 3)
                for (int x = 0; x < e.Width; x += 3)
                {
                    Color ce = e.GetPixel(x, y), ca = a.GetPixel(x, y);
                    if (Math.Abs(ce.A - ca.A) > tolerance ||
                        (ce.A != 0 && (Math.Abs(ce.R - ca.R) > tolerance || Math.Abs(ce.G - ca.G) > tolerance ||
                                       Math.Abs(ce.B - ca.B) > tolerance)))
                        Assert.Fail($"pixel {x},{y}: expected {ce}, got {ca}");
                }
            }
        }

        [TestMethod]
        public void JpegIsDecodedToPremultipliedArgb()
        {
            var data = Encode(ImageFormat.Jpeg, false);

            using (var tile = (GMapImage) GMapImageProxy.Instance.FromArray(data))
            using (var reference = Image.FromStream(new MemoryStream(data), true, true))
            {
                Assert.IsNotNull(tile);
                Assert.AreEqual(PixelFormat.Format32bppPArgb, tile.Img.PixelFormat);
                Assert.AreEqual(new Size(256, 256), tile.Img.Size);
                // the original bytes are kept for the tile caches
                CollectionAssert.AreEqual(data, tile.Data.ToArray());
                AssertSamePixels(reference, tile.Img, 0);
            }
        }

        [TestMethod]
        public void PngKeepsAlpha()
        {
            var data = Encode(ImageFormat.Png, true);

            using (var tile = (GMapImage) GMapImageProxy.Instance.FromArray(data))
            using (var reference = Image.FromStream(new MemoryStream(data), true, true))
            {
                Assert.AreEqual(PixelFormat.Format32bppPArgb, tile.Img.PixelFormat);
                // premultiplying rounds the colour of translucent pixels
                AssertSamePixels(reference, tile.Img, 3);
            }
        }

        [TestMethod]
        public void ImageDoesNotDependOnTheSourceStream()
        {
            var data = Encode(ImageFormat.Jpeg, false);
            var stream = new MemoryStream((byte[]) data.Clone());

            using (var tile = (GMapImage) GMapImageProxy.Instance.FromStream(stream))
            {
                stream.Dispose();
                using (var reference = Image.FromStream(new MemoryStream(data), true, true))
                    AssertSamePixels(reference, tile.Img, 0);
            }
        }

        [TestMethod]
        public void ColorMatrixIsApplied()
        {
            var data = Encode(ImageFormat.Png, false);
            var old = GMapImageProxy.Instance.ColorMatrix;
            try
            {
                // invert
                GMapImageProxy.Instance.ColorMatrix = new ColorMatrix(new[]
                {
                    new float[] {-1, 0, 0, 0, 0},
                    new float[] {0, -1, 0, 0, 0},
                    new float[] {0, 0, -1, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {1, 1, 1, 0, 1},
                });

                using (var tile = (GMapImage) GMapImageProxy.Instance.FromArray(data))
                using (var argb = ToArgb(tile.Img))
                {
                    Assert.AreEqual(PixelFormat.Format32bppPArgb, tile.Img.PixelFormat);
                    var c = argb.GetPixel(200, 10);
                    Assert.AreEqual(255 - 200, c.R, 1);
                    Assert.AreEqual(255 - 10, c.G, 1);
                }
            }
            finally
            {
                GMapImageProxy.Instance.ColorMatrix = old;
            }
        }

        [TestMethod]
        public void CorruptDataIsRejected()
        {
            var data = Encode(ImageFormat.Jpeg, false);
            Array.Resize(ref data, 600);

            Assert.IsNull(GMapImageProxy.Instance.FromArray(new byte[] {1, 2, 3, 4, 5, 6, 7, 8}));
            Assert.IsNull(GMapImageProxy.Instance.FromArray(data));
        }
    }
}
