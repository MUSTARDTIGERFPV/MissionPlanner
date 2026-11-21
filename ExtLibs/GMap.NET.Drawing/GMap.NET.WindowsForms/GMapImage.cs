
namespace GMap.NET.WindowsForms
{
   using System.Drawing;
   using System.IO;
   using System.Drawing.Imaging;
   using System.Drawing.Drawing2D;
   using System;
   using System.Diagnostics;
   using GMap.NET.Internals;
   using GMap.NET.MapProviders;

   /// <summary>
   /// image abstraction
   /// </summary>
   public class GMapImage : PureImage
   {
      public Image Img;

      public override void Dispose()
      {
         if(Img != null)
         {
            Img.Dispose();
            Img = null;
         }

         if(Data != null)
         {
            Data.Dispose();
            Data = null;
         }
      }
   }

   /// <summary>
   /// image abstraction proxy
   /// </summary>
   public class GMapImageProxy : PureImageProxy
   {
      GMapImageProxy()
      {

      }

      public static void Enable()
      {
          GMapProvider.TileImageProxy = Instance;
      }

      public static readonly GMapImageProxy Instance = new GMapImageProxy();

#if !PocketPC
       public ColorMatrix ColorMatrix;
#endif

      static readonly bool Win7OrLater = Stuff.IsRunningOnWin7orLater();

      public override PureImage FromStream(Stream stream)
      {
         GMapImage ret = null;
         try
         {
#if !PocketPC
            Image m = Image.FromStream(stream, true, Win7OrLater ? false : true);
#else
            Image m = new Bitmap(stream);
#endif
            if(m != null)
            {
               ret = new GMapImage();
#if !PocketPC
               // decode now, on the loader thread, so the first paint does not
               // pay for it on the UI thread
               ret.Img = DecodeToPArgb(m, ColorMatrix);
#else
               ret.Img = m;
#endif
            }

         }
         catch(Exception ex)
         {
            ret = null;
            Debug.WriteLine("FromStream: " + ex.ToString());
         }

         return ret;
      }

      public override bool Save(Stream stream, GMap.NET.PureImage image)
      {
         GMapImage ret = image as GMapImage;
         bool ok = true;

         if(ret.Img != null)
         {
            // try png
            try
            {
               ret.Img.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch
            {
               // try jpeg
               try
               {
                  stream.Seek(0, SeekOrigin.Begin);
                  ret.Img.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
               }
               catch
               {
                  ok = false;
               }
            }
         }
         else
         {
            ok = false;
         }

         return ok;
      }

#if !PocketPC
      /// <summary>
      /// Decodes the image into a premultiplied 32bpp bitmap, applying the color
      /// matrix if one is set, and disposes the original.
      /// Image.FromStream without validation decodes lazily on the first DrawImage,
      /// which for tiles is the UI thread; 32bppPArgb is also the format GDI+
      /// blits fastest onto a screen surface.
      /// </summary>
      static Bitmap DecodeToPArgb(Image original, ColorMatrix matrix)
      {
         using(original) // destroy original
         {
            Bitmap newBitmap = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppPArgb);
            try
            {
               newBitmap.SetResolution(original.HorizontalResolution, original.VerticalResolution);

               using(Graphics g = Graphics.FromImage(newBitmap))
               {
                  g.CompositingMode = CompositingMode.SourceCopy;
                  g.InterpolationMode = InterpolationMode.NearestNeighbor;
                  g.PixelOffsetMode = PixelOffsetMode.Half;

                  var rect = new Rectangle(0, 0, original.Width, original.Height);
                  if(matrix != null)
                  {
                     // set the color matrix attribute
                     using(ImageAttributes attributes = new ImageAttributes())
                     {
                        attributes.SetColorMatrix(matrix);
                        g.DrawImage(original, rect, 0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                     }
                  }
                  else
                  {
                     g.DrawImage(original, rect, 0, 0, original.Width, original.Height, GraphicsUnit.Pixel);
                  }
               }
            }
            catch
            {
               newBitmap.Dispose();
               throw;
            }

            return newBitmap;
         }
      }
#endif
   }
}
