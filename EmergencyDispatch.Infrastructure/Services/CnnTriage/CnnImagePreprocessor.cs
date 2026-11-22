using SkiaSharp;

namespace EmergencyDispatch.Infrastructure.Services.CnnTriage;

/// <summary>Ảnh đã giải mã: RGBA 8 bit, alpha không nhân sẵn (bỏ alpha giống PIL convert("RGB"))</summary>
public sealed record DecodedImage(byte[] Rgba, int Width, int Height);

/// <summary>
/// Tiền xử lý giống pipeline Python lúc train / YoloService:
/// ResNet-50: Resize((512, 512)) bilinear → ToTensor → Normalize(ImageNet);
/// YOLO11n-cls: Resize(224) theo cạnh ngắn (torchvision) → CenterCrop(224) → ToTensor (không chuẩn hoá thêm)
/// </summary>
public static class CnnImagePreprocessor
{
    public const int ResNetSize = 512;
    public const int YoloSize = 224;

    /// <summary>
    /// Ảnh lớn hơn ~4 MP được giải mã ở tỉ lệ 1/2, 1/4, 1/8 (JPEG / WebP hỗ trợ giải mã thu nhỏ) để giữ RAM thấp trên Render:
    /// ảnh 12 MP chiếm 48 MB nếu giải mã đủ, 12 MB ở tỉ lệ 1/2 — vẫn lớn hơn nhiều so với 512 px model cần
    /// </summary>
    public const long TargetDecodedPixels = 4_200_000;

    /// <summary>Định dạng không giải mã thu nhỏ được (PNG...) vượt mức này thì từ chối</summary>
    public const long MaxDecodedPixels = 40_000_000;

    private static readonly float[] ImageNetMean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] ImageNetStd = { 0.229f, 0.224f, 0.225f };

    public static DecodedImage Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Không nhận dạng được định dạng ảnh");
        var size = codec.Info.Size;
        if (size.Width <= 0 || size.Height <= 0) throw new InvalidDataException("Ảnh rỗng");

        if (Pixels(size) > TargetDecodedPixels)
        {
            foreach (var scale in new[] { 0.5f, 0.25f, 0.125f })
            {
                var scaled = codec.GetScaledDimensions(scale);
                if (Pixels(scaled) >= Pixels(size) || scaled.Width <= 0 || scaled.Height <= 0) break; // không hỗ trợ thu nhỏ
                size = scaled;
                if (Pixels(size) <= TargetDecodedPixels) break;
            }
            if (Pixels(size) > MaxDecodedPixels)
                throw new InvalidDataException($"Image too large: {codec.Info.Width}x{codec.Info.Height}");
        }

        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var rgba = new byte[info.BytesSize];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(rgba, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var result = codec.GetPixels(info, handle.AddrOfPinnedObject());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                throw new InvalidDataException($"Không giải mã được ảnh: {result}");
        }
        finally
        {
            handle.Free();
        }
        return new DecodedImage(rgba, size.Width, size.Height);
    }

    private static long Pixels(SKSizeI size) => (long)size.Width * size.Height;

    /// <summary>Tensor [1,3,512,512] cho ResNet-50 (chuẩn hoá ImageNet)</summary>
    public static float[] ResNetTensor(DecodedImage image)
    {
        var rgb = PillowResampler.ResizeBilinear(image.Rgba, image.Width, image.Height, 4, ResNetSize, ResNetSize);
        return ToChw(rgb, ResNetSize, ResNetSize, ImageNetMean, ImageNetStd);
    }

    /// <summary>Tensor [1,3,224,224] cho YOLO11n-cls (chỉ chia 255)</summary>
    public static float[] YoloTensor(DecodedImage image)
    {
        var (w, h) = TorchvisionShortSideSize(image.Width, image.Height, YoloSize);
        var resized = PillowResampler.ResizeBilinear(image.Rgba, image.Width, image.Height, 4, w, h);
        var (left, top) = CenterCropOffset(w, h, YoloSize);
        var crop = new byte[YoloSize * YoloSize * 3];
        for (var y = 0; y < YoloSize; y++)
            Array.Copy(resized, ((y + top) * w + left) * 3, crop, y * YoloSize * 3, YoloSize * 3);
        return ToChw(crop, YoloSize, YoloSize, null, null);
    }

    /// <summary>torchvision _compute_resized_output_size với size là số nguyên: cạnh ngắn = size, cạnh dài = int(size * dài / ngắn)</summary>
    public static (int Width, int Height) TorchvisionShortSideSize(int width, int height, int size)
    {
        var (shortSide, longSide) = width <= height ? (width, height) : (height, width);
        var newLong = (int)(size * (double)longSide / shortSide);
        return width <= height ? (size, newLong) : (newLong, size);
    }

    /// <summary>torchvision center_crop: int(round((dài - crop) / 2.0)), Python round = làm tròn về số chẵn</summary>
    public static (int Left, int Top) CenterCropOffset(int width, int height, int crop)
        => ((int)Math.Round((width - crop) / 2.0, MidpointRounding.ToEven),
            (int)Math.Round((height - crop) / 2.0, MidpointRounding.ToEven));

    private static float[] ToChw(byte[] rgb, int width, int height, float[]? mean, float[]? std)
    {
        var plane = width * height;
        var tensor = new float[3 * plane];
        for (var i = 0; i < plane; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                var v = rgb[i * 3 + c] / 255f;
                tensor[c * plane + i] = mean is null ? v : (v - mean[c]) / std![c];
            }
        }
        return tensor;
    }
}
