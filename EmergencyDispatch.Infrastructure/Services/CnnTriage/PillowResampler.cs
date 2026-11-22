namespace EmergencyDispatch.Infrastructure.Services.CnnTriage;

/// <summary>
/// Thu nhỏ / phóng ảnh 8 bit giống hệt Pillow Image.resize(..., BILINEAR) (Resample.c: hệ số double → số nguyên
/// PRECISION_BITS = 22, qua ngang trước rồi dọc, làm tròn + kẹp 0–255 sau mỗi lượt). Model CNN được train với
/// tiền xử lý của Pillow / torchvision nên phải tái tạo đúng để xác suất khớp ngưỡng 0,90
/// </summary>
public static class PillowResampler
{
    private const int PrecisionBits = 32 - 8 - 2;

    /// <summary>
    /// src: điểm ảnh liên tiếp, mỗi điểm <paramref name="srcChannels"/> byte (3 = RGB, 4 = RGBA; chỉ lấy 3 kênh đầu).
    /// Trả về RGB (3 byte / điểm), kích thước newWidth x newHeight
    /// </summary>
    public static byte[] ResizeBilinear(ReadOnlySpan<byte> src, int width, int height, int srcChannels, int newWidth, int newHeight)
    {
        if (width <= 0 || height <= 0 || newWidth <= 0 || newHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Kích thước ảnh phải > 0");
        if (srcChannels is not (3 or 4)) throw new ArgumentOutOfRangeException(nameof(srcChannels));
        if (src.Length < (long)width * height * srcChannels) throw new ArgumentException("Bộ đệm ảnh thiếu dữ liệu", nameof(src));

        var needHorizontal = newWidth != width;
        var needVertical = newHeight != height;

        if (!needHorizontal && !needVertical) return ToRgb(src, width, height, srcChannels);

        var (ksizeV, boundsV, kkV) = PrecomputeCoeffs(height, newHeight);

        byte[] current;
        int curWidth, curHeight, curChannels;
        if (needHorizontal)
        {
            var (ksizeH, boundsH, kkH) = PrecomputeCoeffs(width, newWidth);
            // Chỉ các hàng mà lượt dọc dùng tới (giống Pillow: ybox_first … ybox_last)
            var yFirst = boundsV[0];
            var yLast = boundsV[newHeight * 2 - 2] + boundsV[newHeight * 2 - 1];
            if (!needVertical)
            {
                yFirst = 0;
                yLast = height;
            }
            else
            {
                for (var i = 0; i < newHeight; i++) boundsV[i * 2] -= yFirst;
            }

            var rows = yLast - yFirst;
            current = new byte[newWidth * rows * 3];
            for (var y = 0; y < rows; y++)
            {
                var inRow = (y + yFirst) * width * srcChannels;
                var outRow = y * newWidth * 3;
                for (var xx = 0; xx < newWidth; xx++)
                {
                    var xmin = boundsH[xx * 2];
                    var xmax = boundsH[xx * 2 + 1];
                    var k = xx * ksizeH;
                    int ss0 = 1 << (PrecisionBits - 1), ss1 = ss0, ss2 = ss0;
                    for (var x = 0; x < xmax; x++)
                    {
                        var p = inRow + (x + xmin) * srcChannels;
                        var w = kkH[k + x];
                        ss0 += src[p] * w;
                        ss1 += src[p + 1] * w;
                        ss2 += src[p + 2] * w;
                    }
                    var o = outRow + xx * 3;
                    current[o] = Clip8(ss0);
                    current[o + 1] = Clip8(ss1);
                    current[o + 2] = Clip8(ss2);
                }
            }
            curWidth = newWidth;
            curHeight = rows;
            curChannels = 3;
            if (!needVertical) return current;
        }
        else
        {
            current = ToRgb(src, width, height, srcChannels);
            curWidth = width;
            curHeight = height;
            curChannels = 3;
        }

        _ = curHeight;
        var output = new byte[newWidth * newHeight * 3];
        for (var yy = 0; yy < newHeight; yy++)
        {
            var ymin = boundsV[yy * 2];
            var ymax = boundsV[yy * 2 + 1];
            var k = yy * ksizeV;
            var outRow = yy * newWidth * 3;
            for (var xx = 0; xx < curWidth; xx++)
            {
                int ss0 = 1 << (PrecisionBits - 1), ss1 = ss0, ss2 = ss0;
                for (var y = 0; y < ymax; y++)
                {
                    var p = ((y + ymin) * curWidth + xx) * curChannels;
                    var w = kkV[k + y];
                    ss0 += current[p] * w;
                    ss1 += current[p + 1] * w;
                    ss2 += current[p + 2] * w;
                }
                var o = outRow + xx * 3;
                output[o] = Clip8(ss0);
                output[o + 1] = Clip8(ss1);
                output[o + 2] = Clip8(ss2);
            }
        }
        return output;
    }

    /// <summary>precompute_coeffs + normalize_coeffs_8bpc của Pillow cho bộ lọc bilinear (support = 1)</summary>
    private static (int Ksize, int[] Bounds, int[] Kk) PrecomputeCoeffs(int inSize, int outSize)
    {
        const double filterSupport = 1.0;
        var scale = (double)inSize / outSize;
        var filterScale = scale < 1.0 ? 1.0 : scale;
        var support = filterSupport * filterScale;
        var ksize = (int)Math.Ceiling(support) * 2 + 1;

        var prekk = new double[outSize * ksize];
        var bounds = new int[outSize * 2];
        for (var xx = 0; xx < outSize; xx++)
        {
            var center = (xx + 0.5) * scale;
            var ww = 0.0;
            var ss = 1.0 / filterScale;
            var xmin = (int)(center - support + 0.5);
            if (xmin < 0) xmin = 0;
            var xmax = (int)(center + support + 0.5);
            if (xmax > inSize) xmax = inSize;
            xmax -= xmin;
            var k = xx * ksize;
            int x;
            for (x = 0; x < xmax; x++)
            {
                var w = Bilinear((x + xmin - center + 0.5) * ss);
                prekk[k + x] = w;
                ww += w;
            }
            for (x = 0; x < xmax; x++)
            {
                if (ww != 0.0) prekk[k + x] /= ww;
            }
            for (; x < ksize; x++) prekk[k + x] = 0;
            bounds[xx * 2] = xmin;
            bounds[xx * 2 + 1] = xmax;
        }

        var kk = new int[prekk.Length];
        for (var i = 0; i < prekk.Length; i++)
        {
            kk[i] = prekk[i] < 0
                ? (int)(-0.5 + prekk[i] * (1 << PrecisionBits))
                : (int)(0.5 + prekk[i] * (1 << PrecisionBits));
        }
        return (ksize, bounds, kk);
    }

    private static double Bilinear(double x)
    {
        if (x < 0.0) x = -x;
        return x < 1.0 ? 1.0 - x : 0.0;
    }

    private static byte Clip8(int value)
    {
        if (value >= (1 << PrecisionBits << 8)) return 255;
        if (value <= 0) return 0;
        return (byte)(value >> PrecisionBits);
    }

    private static byte[] ToRgb(ReadOnlySpan<byte> src, int width, int height, int srcChannels)
    {
        var n = width * height;
        var rgb = new byte[n * 3];
        if (srcChannels == 3)
        {
            src[..(n * 3)].CopyTo(rgb);
            return rgb;
        }
        for (var i = 0; i < n; i++)
        {
            rgb[i * 3] = src[i * 4];
            rgb[i * 3 + 1] = src[i * 4 + 1];
            rgb[i * 3 + 2] = src[i * 4 + 2];
        }
        return rgb;
    }
}
