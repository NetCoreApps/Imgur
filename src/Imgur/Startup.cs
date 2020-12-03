using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Funq;
using ServiceStack;
using ServiceStack.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using ServiceStack.Text;

//Entire C# source code for Imgur backend - there is no other .cs :)
namespace Imgur
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }

        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .Build();
    }

    public class Startup
    {
        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseStaticFiles();

            app.UseServiceStack(new AppHost());
        }
    }

    public class AppHost : AppHostBase
    {
        public AppHost() : base("Image Resizer", typeof(AppHost).Assembly) {}
        public override void Configure(Container container) {}
    }

    [Route("/upload")]
    public class Upload
    {
        public string Url { get; set; }
    }

    [Route("/images")]
    public class Images { }

    [Route("/resize/{Id}")]
    public class Resize
    {
        public string Id { get; set; }
        public string Size { get; set; }
    }

    [Route("/reset")]
    public class Reset { }

    [Route("/delete/{Id}")]
    public class DeleteUpload
    {
        public string Id { get; set; }
    }

    public class ImageService : Service
    {
        const int ThumbnailSize = 100;
        readonly string UploadsDir = "wwwroot/uploads";
        readonly string ThumbnailsDir = "wwwroot/uploads/thumbnails";
        readonly List<string> ImageSizes = new[] { "320x480", "640x960", "640x1136", "768x1024", "1536x2048" }.ToList();

        public object Get(Images request)
        {
            return VirtualFiles.GetDirectory(UploadsDir).Files.Map(x => x.Name);
        }

        public object Post(Upload request)
        {
            if (request.Url != null)
            {
                using (var ms = new MemoryStream(request.Url.GetBytesFromUrl()))
                {
                    WriteImage(ms);
                }
            }

            foreach (var uploadedFile in Request.Files.Where(uploadedFile => uploadedFile.ContentLength > 0))
            {
                using (var ms = new MemoryStream())
                {
                    uploadedFile.WriteTo(ms);
                    WriteImage(ms);
                }
            }

            return HttpResult.Redirect("/");
        }

        private void WriteImage(Stream ms)
        {
            ms.Position = 0;
            var hash = ms.ToMd5Hash();

            ms.Position = 0;
            var fileName = hash + ".png";
            using (var img = Image.FromStream(ms))
            {
                using (var msPng = MemoryStreamFactory.GetStream())
                {
                    img.Save(msPng, ImageFormat.Png);
                    msPng.Position = 0;
                    VirtualFiles.WriteFile(UploadsDir.CombineWith(fileName), msPng);
                }

                var stream = Resize(img, ThumbnailSize, ThumbnailSize);
                VirtualFiles.WriteFile(ThumbnailsDir.CombineWith(fileName), stream);

                ImageSizes.ForEach(x => VirtualFiles.WriteFile(
                    UploadsDir.CombineWith(x).CombineWith(hash + ".png"),
                    Get(new Resize { Id = hash, Size = x }).ReadFully()));
            }
        }

        [AddHeader(ContentType = "image/png")]
        public Stream Get(Resize request)
        {
            var imageFile = VirtualFiles.GetFile(UploadsDir.CombineWith(request.Id + ".png"));
            if (request.Id == null || imageFile == null)
                throw HttpError.NotFound(request.Id + " was not found");

            using (var stream = imageFile.OpenRead())
            using (var img = Image.FromStream(stream))
            {
                var parts = request.Size?.Split('x');
                int width = img.Width;
                int height = img.Height;

                if (parts != null && parts.Length > 0)
                    int.TryParse(parts[0], out width);

                if (parts != null && parts.Length > 1)
                    int.TryParse(parts[1], out height);

                return Resize(img, width, height);
            }
        }

        public static Stream Resize(Image img, int newWidth, int newHeight)
        {
            if (newWidth != img.Width || newHeight != img.Height)
            {
                var ratioX = (double)newWidth / img.Width;
                var ratioY = (double)newHeight / img.Height;
                var ratio = Math.Max(ratioX, ratioY);
                var width = (int)(img.Width * ratio);
                var height = (int)(img.Height * ratio);

                var newImage = new Bitmap(width, height);
                Graphics.FromImage(newImage).DrawImage(img, 0, 0, width, height);
                img = newImage;

                if (img.Width != newWidth || img.Height != newHeight)
                {
                    var startX = (Math.Max(img.Width, newWidth) - Math.Min(img.Width, newWidth)) / 2;
                    var startY = (Math.Max(img.Height, newHeight) - Math.Min(img.Height, newHeight)) / 2;
                    img = Crop(img, newWidth, newHeight, startX, startY);
                }
            }

            var ms = new MemoryStream();
            img.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            return ms;
        }

        public static Image Crop(Image Image, int newWidth, int newHeight, int startX = 0, int startY = 0)
        {
            if (Image.Height < newHeight)
                newHeight = Image.Height;

            if (Image.Width < newWidth)
                newWidth = Image.Width;

            using (var bmp = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb))
            {
                bmp.SetResolution(72, 72);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(Image, new Rectangle(0, 0, newWidth, newHeight), startX, startY, newWidth, newHeight, GraphicsUnit.Pixel);

                    var ms = new MemoryStream();
                    bmp.Save(ms, ImageFormat.Png);
                    Image.Dispose();
                    var outimage = Image.FromStream(ms);
                    return outimage;
                }
            }
        }

        public object Any(DeleteUpload request)
        {
            var file = request.Id + ".png";
            var filesToDelete = new[] { UploadsDir.CombineWith(file), ThumbnailsDir.CombineWith(file) }.ToList();
            ImageSizes.Each(x => filesToDelete.Add(UploadsDir.CombineWith(x, file)));
            VirtualFiles.DeleteFiles(filesToDelete);

            return HttpResult.Redirect("/");
        }

        public object Any(Reset request)
        {
            VirtualFiles.DeleteFiles(VirtualFiles.GetDirectory(UploadsDir).GetAllMatchingFiles("*.png"));
            VirtualFileSources.GetFile("preset-urls.txt").ReadAllText().ReadLines().ToList()
                .ForEach(url => WriteImage(new MemoryStream(url.Trim().GetBytesFromUrl())));

            return HttpResult.Redirect("/");
        }
    }
}
