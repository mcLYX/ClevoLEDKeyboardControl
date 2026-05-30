namespace ColorfulLedKeyboard.Tray;

internal static class AlbumColorExtractor
{
    public static async Task<string> ExtractAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient();
        await using var stream = await http.GetStreamAsync(imageUrl, cancellationToken);
        return Extract(stream);
    }

    public static string Extract(Stream stream)
    {
        using var image = new Bitmap(stream);
        var buckets = new Dictionary<int, (int Count, int R, int G, int B)>();
        var stepX = Math.Max(1, image.Width / 48);
        var stepY = Math.Max(1, image.Height / 48);
        for (var y = 0; y < image.Height; y += stepY)
        {
            for (var x = 0; x < image.Width; x += stepX)
            {
                var color = image.GetPixel(x, y);
                var max = Math.Max(color.R, Math.Max(color.G, color.B));
                var min = Math.Min(color.R, Math.Min(color.G, color.B));
                if (max < 35 || max - min < 18)
                {
                    continue;
                }

                var key = ((color.R / 32) << 16) | ((color.G / 32) << 8) | (color.B / 32);
                buckets.TryGetValue(key, out var bucket);
                bucket.Count++;
                bucket.R += color.R;
                bucket.G += color.G;
                bucket.B += color.B;
                buckets[key] = bucket;
            }
        }

        if (buckets.Count == 0)
        {
            return "#FFFFFF";
        }

        var best = buckets.Values
            .OrderByDescending(bucket => bucket.Count)
            .ThenByDescending(bucket => bucket.R + bucket.G + bucket.B)
            .First();
        var r = Math.Clamp(best.R / best.Count, 0, 255);
        var g = Math.Clamp(best.G / best.Count, 0, 255);
        var b = Math.Clamp(best.B / best.Count, 0, 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
