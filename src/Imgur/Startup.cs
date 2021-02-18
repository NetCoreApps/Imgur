using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Funq;
using ServiceStack;
using ServiceStack.IO;
using Microsoft.Extensions.Hosting;
using ServiceStack.Text;
using SkiaSharp;

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
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
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
            using var img = SKBitmap.Decode(ms);
            
            using (var msPng = MemoryStreamFactory.GetStream())
            {
                img.Encode(msPng, SKEncodedImageFormat.Png, 75);
                msPng.Position = 0;
                VirtualFiles.WriteFile(UploadsDir.CombineWith(fileName), msPng);
            }

            var stream = Resize(img, ThumbnailSize, ThumbnailSize);
            VirtualFiles.WriteFile(ThumbnailsDir.CombineWith(fileName), stream);

            ImageSizes.ForEach(x => VirtualFiles.WriteFile(
                UploadsDir.CombineWith(x).CombineWith(hash + ".png"),
                Get(new Resize { Id = hash, Size = x }).ReadFully()));
        }

        [AddHeader(ContentType = "image/png")]
        public Stream Get(Resize request)
        {
            var imageFile = VirtualFiles.GetFile(UploadsDir.CombineWith(request.Id + ".png"));
            if (request.Id == null || imageFile == null)
                throw HttpError.NotFound(request.Id + " was not found");

            using var stream = imageFile.OpenRead();
            using var img = SKBitmap.Decode(stream);
            var parts = request.Size?.Split('x');
            int width = img.Width;
            int height = img.Height;

            if (parts != null && parts.Length > 0)
                int.TryParse(parts[0], out width);

            if (parts != null && parts.Length > 1)
                int.TryParse(parts[1], out height);

            return Resize(img, width, height);
        }

        public static Stream Resize(SKBitmap img, int newWidth, int newHeight)
        {
            if (newWidth != img.Width || newHeight != img.Height)
            {
                var ratioX = (double)newWidth / img.Width;
                var ratioY = (double)newHeight / img.Height;
                var ratio = Math.Max(ratioX, ratioY);
                var width = (int)(img.Width * ratio);
                var height = (int)(img.Height * ratio);

                img = img.Resize(new SKImageInfo(width, height), SKFilterQuality.Medium);
                if (img.Width != newWidth || img.Height != newHeight)
                {
                    img = Crop(img, newWidth, newHeight); // resized + clipped
                }
            }

            var pngStream = img.Encode(SKEncodedImageFormat.Png, 75).AsStream();
            return pngStream;
        }
        
        public static SKBitmap Crop(SKBitmap img, int newWidth, int newHeight)
        {
            if (img.Width < newWidth)
                newWidth = img.Width;

            if (img.Height < newHeight)
                newHeight = img.Height;

            var startX = (Math.Max(img.Width, newWidth) - Math.Min(img.Width, newWidth)) / 2;
            var startY = (Math.Max(img.Height, newHeight) - Math.Min(img.Height, newHeight)) / 2;

            var croppedBitmap = new SKBitmap(newWidth, newHeight);
            var source = new SKRect(startX, startY, newWidth + startX, newHeight + startY);
            var dest = new SKRect(0, 0, newWidth, newHeight);
            using var canvas = new SKCanvas(croppedBitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(img, source, dest);
            img.Dispose();
            
            return croppedBitmap;
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
