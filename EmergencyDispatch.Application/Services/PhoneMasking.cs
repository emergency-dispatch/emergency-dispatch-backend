namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Chuẩn hóa và che số điện thoại người báo (BR-32: hiển thị dạng 098***456, chỉ lộ số đầy đủ khi bấm "Hiện số")
/// </summary>
public static class PhoneMasking
{
    /// <summary>
    /// Chuẩn hóa về chuỗi chữ số đầu 0 (VD "+84 981 234 456" → "0981234456"); null nếu không có chữ số
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;

        if (digits.StartsWith("84") && digits.Length >= 11)
        {
            digits = "0" + digits[2..];
        }

        return digits;
    }

    /// <summary>
    /// Che phần giữa số điện thoại: giữ 3 số đầu và 3 số cuối
    /// </summary>
    public static string? Mask(string? phone)
    {
        var digits = Normalize(phone);
        if (digits == null) return null;
        if (digits.Length <= 6) return new string('*', digits.Length);

        return $"{digits[..3]}***{digits[^3..]}";
    }
}
