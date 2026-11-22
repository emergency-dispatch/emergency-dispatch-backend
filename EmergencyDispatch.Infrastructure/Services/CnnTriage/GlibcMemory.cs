using System.Runtime.InteropServices;

namespace EmergencyDispatch.Infrastructure.Services.CnnTriage;

/// <summary>
/// Trên Linux (glibc) bộ đệm trung gian của ONNX Runtime được malloc/free mỗi lần chạy; mặc định glibc nâng dần ngưỡng
/// mmap nên các khối lớn rơi vào heap và không trả lại hệ điều hành (RAM tăng thêm ~160 MB sau vài ảnh).
/// Cố định ngưỡng mmap = 128 KB (khối lớn luôn mmap → free là trả ngay) và malloc_trim sau mỗi lần chạy.
/// Windows / macOS / musl: bỏ qua
/// </summary>
internal static class GlibcMemory
{
    private const int M_TRIM_THRESHOLD = -1;
    private const int M_MMAP_THRESHOLD = -3;
    private const int Threshold = 128 * 1024;
    private static bool _configured;
    private static bool _unsupported = !OperatingSystem.IsLinux();

    public static void ConfigureForLargeBuffers()
    {
        if (_configured || _unsupported) return;
        try
        {
            mallopt(M_MMAP_THRESHOLD, Threshold);
            mallopt(M_TRIM_THRESHOLD, Threshold);
            _configured = true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unsupported = true;
        }
    }

    public static void Trim()
    {
        if (_unsupported) return;
        try
        {
            malloc_trim(0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unsupported = true;
        }
    }

    [DllImport("libc.so.6", EntryPoint = "mallopt")]
    private static extern int mallopt(int param, int value);

    [DllImport("libc.so.6", EntryPoint = "malloc_trim")]
    private static extern int malloc_trim(nuint pad);
}
