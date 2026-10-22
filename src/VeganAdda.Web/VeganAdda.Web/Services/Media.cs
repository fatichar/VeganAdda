using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using VeganAdda.Contracts;
using VeganAdda.Web.Data;
using VeganAdda.Web.Features.Profiles;
using VeganAdda.Web.Features.Shared;

namespace VeganAdda.Web.Services;

public static class Media
{
    private static readonly SemaphoreSlim UploadSlots = new(2, 2);
    public static void MapMedia(this WebApplication app)
    {
        var root = Path.GetFullPath(app.Configuration["Storage:MediaPath"] ?? Path.Combine(app.Environment.ContentRootPath, "uploads"));
        app.Configuration["Storage:MediaPath"] = root;
        Directory.CreateDirectory(root);
        app.MapPost("/api/upload", async (HttpContext ctx, IActor actor, CommunityDb db, IAntiforgery csrf) =>
        {
            await csrf.ValidateRequestAsync(ctx);
            if (actor.Id is not { } id || !await db.Accounts.AnyAsync(a => a.Id == id && !a.Suspended)) return Results.Unauthorized();
            if (!ctx.Request.HasFormContentType) throw new ApiException(400, "Choose a supported image file.");
            var purpose = ctx.Request.Query["purpose"].ToString();
            if (purpose is not ("" or "report")) throw new ApiException(400, "Choose a supported upload purpose.");
            var reportScreenshot = purpose == "report";
            if (reportScreenshot && !await db.Profiles.Where(BasicProfile.Complete).AnyAsync(p => p.Id == id))
                throw new ApiException(409, "Complete your basic profile first to use VeganAdda.");
            var destination = reportScreenshot ? Path.Combine(root, "reports") : root;
            Directory.CreateDirectory(destination);
            var form = await ctx.Request.ReadFormAsync(); var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0 || file.Length > 2 * 1024 * 1024) throw new ApiException(400, "Choose a JPG, PNG or WebP image smaller than 2 MB.");
            if (file.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) throw new ApiException(400, "Choose a JPG, PNG or WebP image.");
            if (!await UploadSlots.WaitAsync(0)) throw new ApiException(429, "Photo processing is busy. Please try again in a moment.");
            try
            {
                await using var stream = file.OpenReadStream();
                using var data = SKData.Create(stream); using var codec = SKCodec.Create(data);
                if (codec == null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp)) throw new ApiException(400, "The file is not a supported image.");
                var info = codec.Info;
                if (info.Width > 6000 || info.Height > 6000 || (long)info.Width * info.Height > 16000000) throw new ApiException(400, "Choose an image up to 16 megapixels, with sides at most 6000 pixels.");
                using var decoded = SKBitmap.Decode(codec) ?? throw new ApiException(400, "This image could not be decoded.");
                // Normalize all uploads to freshly encoded pixels. No source metadata or animation is retained.
                var scale = Math.Min(1d, 1200d / Math.Max(info.Width, info.Height));
                var rotated = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
                using var oriented = new SKBitmap(rotated ? info.Height : info.Width, rotated ? info.Width : info.Height);
                using (var canvas = new SKCanvas(oriented))
                {
                    switch (codec.EncodedOrigin)
                    {
                        case SKEncodedOrigin.TopRight: canvas.Translate(info.Width, 0); canvas.Scale(-1, 1); break;
                        case SKEncodedOrigin.BottomRight: canvas.Translate(info.Width, info.Height); canvas.RotateDegrees(180); break;
                        case SKEncodedOrigin.BottomLeft: canvas.Translate(0, info.Height); canvas.Scale(1, -1); break;
                        case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                        case SKEncodedOrigin.RightTop: canvas.Translate(info.Height, 0); canvas.RotateDegrees(90); break;
                        case SKEncodedOrigin.RightBottom: canvas.Translate(info.Height, info.Width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                        case SKEncodedOrigin.LeftBottom: canvas.Translate(0, info.Width); canvas.RotateDegrees(270); break;
                    }
                    using var source = SKImage.FromBitmap(decoded);
                    canvas.Clear(SKColors.White); canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear)); canvas.Flush();
                }
                using var resized = oriented.Resize(new SKImageInfo(Math.Max(1, (int)(oriented.Width * scale)), Math.Max(1, (int)(oriented.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear)) ?? throw new ApiException(400, "This image could not be resized.");
                using var image = SKImage.FromBitmap(resized); using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 88);
                var name = Guid.NewGuid().ToString("N") + ".jpg";
                await using (var output = File.Create(Path.Combine(destination, name))) encoded.SaveTo(output);
                await File.WriteAllTextAsync(Path.Combine(destination, name + ".owner-" + id.ToString("N")), "");
                return Results.Ok(new { path = (reportScreenshot ? "/report-media/" : "/media/") + name });
            }
            catch (ArgumentException) { throw new ApiException(400, "This image could not be read."); }
            finally { UploadSlots.Release(); }
        }).DisableAntiforgery();
        app.MapGet("/report-media/{name}", async (string name, CommunityDb db, IActor actor, HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "private, no-store";
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-f0-9]{32}\.jpg$") || actor.Id is not { } id) return Results.NotFound();
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id && !a.Suspended);
            if (account == null) return Results.NotFound();
            var file = Path.Combine(root, "reports", name);
            var owner = File.Exists(file + ".owner-" + id.ToString("N"));
            var admin = account.Admin && await db.Reports.AnyAsync(r => r.Screenshot == "/report-media/" + name);
            return (owner || admin) && File.Exists(file) ? Results.File(file, "image/jpeg") : Results.NotFound();
        });
        app.MapGet("/media/{name}", async (string name, CommunityDb db, IActor actor, ProfileVisibility visibility) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-f0-9]{32}\.(jpg|png|webp)$")) return Results.NotFound();
            var url = "/media/" + name; var id = actor.Id;
            if (id.HasValue && !await db.Accounts.AnyAsync(a => a.Id == id && !a.Suspended)) return Results.NotFound();
            var isAdmin = id.HasValue && await db.Accounts.AnyAsync(a => a.Id == id && a.Admin && !a.Suspended);
            var isModerator = id.HasValue && await db.Accounts.AnyAsync(a => a.Id == id && (a.Admin || (a.Moderator && db.Profiles.Where(BasicProfile.Complete).Any(p => p.Id == a.Id && p.IsVegan == true && p.VeganVerified && !p.TestProfile))) && !a.Suspended);
            var visible = await visibility.CanReadPhoto(url, id, isAdmin, isModerator)                || await db.Events.AnyAsync(e => e.Cover == url && (e.Published || (e.PendingReview && ((isAdmin && !e.Demo) || (isModerator && !e.Demo)))))
                || (id.HasValue && (File.Exists(Path.Combine(root, name + ".owner-" + id.Value.ToString("N"))) || await db.Accounts.AnyAsync(a => a.Id == id && a.Admin)));
            var file = Path.Combine(root, name);
            return visible && File.Exists(file) ? Results.File(file, name.EndsWith(".jpg") ? "image/jpeg" : name.EndsWith(".png") ? "image/png" : "image/webp") : Results.NotFound();
        });
    }
}
