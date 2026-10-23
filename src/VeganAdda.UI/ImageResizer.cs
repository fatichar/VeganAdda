using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using VeganAdda.Contracts;

namespace VeganAdda.UI;

public static class ImageResizer
{
    private record ResizedImage(byte[] Bytes, string Name, string ContentType);

    public static async Task<(Stream Stream, string Name, string ContentType)> ProcessUpload(IJSRuntime js, InputFileChangeEventArgs e, string inputId)
    {
        var file = e.File;
        if (file.Size > 10 * 1024 * 1024)
        {
            throw new ApiException(400, "Please choose an image smaller than 10 MB.");
        }

        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/VeganAdda.UI/image-resizer.js");
            var result = await module.InvokeAsync<ResizedImage?>("resizeImageInput", inputId, 2 * 1024 * 1024, 1200);
            if (result != null && result.Bytes != null && result.Bytes.Length > 0)
            {
                return (new MemoryStream(result.Bytes), result.Name, result.ContentType);
            }
        }
        catch (JSException ex)
        {
            var message = ex.Message;
            if (message.Contains("10 MB") || message.Contains("2 MB") || message.Contains("decoded") || message.Contains("Canvas"))
            {
                throw new ApiException(400, message);
            }
            throw new ApiException(400, "The selected image could not be processed.");
        }
        catch (ApiException)
        {
            throw;
        }
        catch (Exception)
        {
            // Fallback for non-JS environments or when element is not found
        }

        if (file.Size > 2 * 1024 * 1024)
        {
            throw new ApiException(400, "Please choose an image smaller than 2 MB.");
        }

        var memoryStream = new MemoryStream();
        await using (var stream = file.OpenReadStream(2 * 1024 * 1024))
        {
            await stream.CopyToAsync(memoryStream);
        }
        memoryStream.Position = 0;
        return (memoryStream, file.Name, file.ContentType);
    }
}
