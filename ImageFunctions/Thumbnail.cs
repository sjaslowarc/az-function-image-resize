// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

// Learn how to locally debug an Event Grid-triggered function:
//    https://aka.ms/AA30pjh

// Use for local testing:
//   https://{ID}.ngrok.io/runtime/webhooks/EventGrid?functionName=Thumbnail

using Azure.Storage.Blobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ImageFunctions
{
    public static class Thumbnail
    {
        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        private static string GetBlobNameFromUrl(string bloblUrl)
        {
            var uri = new Uri(bloblUrl);
            var blobClient = new BlobClient(uri);
            return blobClient.Name;
        }

        private static IImageEncoder GetEncoder(string extension)
        {
            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");

            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension.ToLower())
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                        encoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }

        private static void ResizeImage(int size, [EventGridTrigger]EventGridEvent eventGridEvent, [Blob("{data.url}", FileAccess.Read)] Stream input, ILogger log) {
                if (input != null)
                {
                    var createdEvent = ((JObject)eventGridEvent.Data).ToObject<StorageBlobCreatedEventData>();
                    var extension = Path.GetExtension(createdEvent.Url);
                    var encoder = GetEncoder(extension);
                    log.LogInformation($"URL is: {createdEvent.Url}");

                    if (encoder != null && createdEvent.Url.Contains("covers/full/"))
                    {
                        var thumbnailWidth = size;
                        var thumbContainerName = Environment.GetEnvironmentVariable("THUMBNAIL_CONTAINER_NAME");
                        var blobServiceClient = new BlobServiceClient(BLOB_STORAGE_CONNECTION_STRING);
                        var blobContainerClient = blobServiceClient.GetBlobContainerClient(thumbContainerName);
                        var blobName = GetBlobNameFromUrl(createdEvent.Url);
                        log.LogInformation($"Blob name is: {blobName}");
                        return;

                        using (var output = new MemoryStream())
                        using (Image<Rgba32> image = Image.Load(input))
                        {
                            var divisor = image.Width / thumbnailWidth;
                            var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                            image.Mutate(x => x.Resize(thumbnailWidth, height));
                            image.Save(output, encoder);
                            output.Position = 0;
                            await blobContainerClient.UploadBlobAsync(thumbnailWidth+"/"+blobName, output);
                        }
                    }
                    else
                    {
                        log.LogInformation($"No encoder support for: {createdEvent.Url}");
                    }
                }
        }

        [FunctionName("Thumbnail")]
        public static async Task Run(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.Read)] Stream input,
            ILogger log)
        {
            try
            {
                ResizeImage(128, eventGridEvent, input, log);
                ResizeImage(512, eventGridEvent, input, log);
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }
    }
}
