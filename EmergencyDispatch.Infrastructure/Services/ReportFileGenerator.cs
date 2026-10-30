using System.Globalization;
using System.Text;
using EmergencyDispatch.Application.DTOs.Report;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>
/// Xuất báo cáo sự cố: CSV (UTF-8 có BOM để Excel đọc đúng tiếng Việt) và PDF (QuestPDF, A4 ngang)
/// </summary>
public class ReportFileGenerator : IReportFileGenerator
{
    // Arial có đủ dấu tiếng Việt trên Windows; Linux thường có DejaVu Sans
    private static readonly string[] FontFamilies = { "Arial", "DejaVu Sans", "Liberation Sans" };

    static ReportFileGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static string Local(DateTime? utc) => utc.HasValue ? utc.Value.AddHours(7).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : string.Empty;

    private static string Minutes(double? m) => m.HasValue ? m.Value.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;

    private static string StatusLabel(IncidentStatus s) => s switch
    {
        IncidentStatus.Completed => "Hoàn thành",
        IncidentStatus.Cancelled => "Đã hủy",
        _ => s.ToString()
    };

    private static string SeverityLabel(SeverityLevel s) => s switch
    {
        SeverityLevel.Unclassified => "Chưa phân loại",
        SeverityLevel.Level1 => "Cấp 1",
        SeverityLevel.Level2 => "Cấp 2",
        SeverityLevel.Level3 => "Cấp 3",
        _ => s.ToString()
    };

    private static string CancelLabel(CancellationReason? r) => r switch
    {
        null => string.Empty,
        CancellationReason.FalseReport => "Báo khống",
        CancellationReason.Duplicate => "Trùng lặp",
        CancellationReason.TestReport => "Thử nghiệm",
        CancellationReason.CitizenCancelled => "Người dân hủy",
        CancellationReason.AutoExpired => "Hết hạn",
        _ => "Khác"
    };

    public byte[] BuildIncidentCsv(IReadOnlyList<IncidentHistoryItemDto> rows, IncidentReportMetaDto meta)
    {
        var sb = new StringBuilder();
        void Line(params string?[] cells) => sb.AppendLine(string.Join(",", cells.Select(Escape)));

        Line(meta.Title);
        Line(meta.FilterDescription);
        Line($"Người xuất: {meta.GeneratedBy}", $"Thời điểm: {Local(meta.GeneratedAtUtc)} (giờ VN)");
        if (meta.Truncated)
        {
            Line($"Lưu ý: chỉ xuất {rows.Count}/{meta.TotalMatched} sự cố đầu tiên. Hãy thu hẹp bộ lọc.");
        }
        sb.AppendLine();

        Line("Mã sự cố", "Tiêu đề", "Địa chỉ", "Mức độ", "AI đề xuất", "Trạng thái", "Lý do hủy",
            "Báo cáo lúc", "Xác minh lúc", "Điều xe lúc", "Tới hiện trường lúc", "Đóng lúc",
            "Phản ứng (phút)", "Xử lý (phút)", "Trạm", "Xe", "Người xác minh", "Người đóng", "Tóm tắt kết quả");

        foreach (var r in rows)
        {
            Line(r.Id.ToString(), r.Title, r.LocationAddress, SeverityLabel(r.Severity),
                r.AiSeverityScore?.ToString(CultureInfo.InvariantCulture), StatusLabel(r.Status), CancelLabel(r.CancellationReason),
                Local(r.ReportedAt), Local(r.VerifiedAt), Local(r.FirstDispatchedAt), Local(r.FirstArrivedAt), Local(r.ClosedAt),
                Minutes(r.ResponseMinutes), Minutes(r.ResolutionMinutes),
                string.Join("; ", r.Stations), string.Join("; ", r.Units),
                r.VerifiedByName, r.ClosedByName, r.ResolutionSummary);
        }

        sb.AppendLine();
        var s = meta.Summary;
        Line("Tổng", s.Total.ToString(CultureInfo.InvariantCulture));
        Line("Hoàn thành", s.Completed.ToString(CultureInfo.InvariantCulture));
        Line("Đã hủy", s.Cancelled.ToString(CultureInfo.InvariantCulture));
        Line("Phản ứng TB (phút)", Minutes(s.AvgResponseMinutes));
        Line("Xử lý TB (phút)", Minutes(s.AvgResolutionMinutes));

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return preamble.Concat(body).ToArray();
    }

    /// <summary>
    /// Bộ dữ liệu thực nghiệm RQ2 / RQ3: mỗi dòng 1 sự cố, thời gian ISO-8601 UTC để nạp vào Excel / Python
    /// </summary>
    public byte[] BuildTelemetryCsv(IReadOnlyList<EmergencyDispatch.Application.DTOs.Benchmark.TelemetryIncidentRowDto> rows)
    {
        static string T(DateTime? d) => d?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty;
        static string N(long? v) => v?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        var sb = new StringBuilder();
        void Line(params string?[] cells) => sb.AppendLine(string.Join(",", cells.Select(Escape)));

        Line("incidentId", "createdAt", "status", "responsibleStation", "isOutOfServiceArea",
            "hasAi", "aiSuccess", "aiProcessedAt", "aiModelLatencyMs", "aiModel", "aiSeverity", "aiEventType",
            "verifiedAt", "verifiedByUserId", "confirmedSeverity", "confirmedEventType", "wasAiOverridden",
            "firstDispatchAt", "algorithm", "unitsDispatched", "firstAcceptedAt", "firstArrivedAt", "closedAt", "cancellationReason",
            "tAiMs", "tOperatorMs", "tDispatchMs", "tResponseMs", "tResolutionMs", "source");

        foreach (var r in rows)
        {
            Line(r.IncidentId.ToString(), T(r.CreatedAt), r.Status.ToString(), r.ResponsibleStation, r.IsOutOfServiceArea.ToString(),
                r.HasAi.ToString(), r.AiSuccess?.ToString(), T(r.AiProcessedAt), N(r.AiModelLatencyMs), r.AiModel, r.AiSeverity?.ToString(), r.AiEventType?.ToString(),
                T(r.VerifiedAt), r.VerifiedByUserId?.ToString(), r.ConfirmedSeverity.ToString(), r.ConfirmedEventType.ToString(), r.WasAiOverridden.ToString(),
                T(r.FirstDispatchAt), r.Algorithm, r.UnitsDispatched.ToString(CultureInfo.InvariantCulture), T(r.FirstAcceptedAt), T(r.FirstArrivedAt), T(r.ClosedAt), r.CancellationReason?.ToString(),
                N(r.TAiMs), N(r.TOperatorMs), N(r.TDispatchMs), N(r.TResponseMs), N(r.TResolutionMs), r.Source.ToString());
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>
    /// Bọc ô CSV; chặn CSV injection (ô bắt đầu bằng = + - @ bị Excel hiểu là công thức)
    /// </summary>
    private static string Escape(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Length > 0 && "=+-@".Contains(v[0]))
        {
            v = "'" + v;
        }
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
    }

    public byte[] BuildIncidentPdf(IReadOnlyList<IncidentHistoryItemDto> rows, IncidentReportMetaDto meta)
    {
        var s = meta.Summary;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontSize(8).FontFamily(FontFamilies));

                page.Header().Column(col =>
                {
                    col.Item().Text(meta.Title).FontSize(16).Bold();
                    col.Item().Text(meta.FilterDescription).FontSize(9).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Người xuất: {meta.GeneratedBy} · {Local(meta.GeneratedAtUtc)} (giờ VN)").FontSize(9).FontColor(Colors.Grey.Darken2);
                    if (meta.Truncated)
                    {
                        col.Item().Text($"Chỉ hiển thị {rows.Count}/{meta.TotalMatched} sự cố đầu tiên — hãy thu hẹp bộ lọc.").FontColor(Colors.Red.Medium);
                    }

                    col.Item().PaddingTop(6).Row(row =>
                    {
                        void Stat(string label, string value) => row.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Column(c =>
                        {
                            c.Item().Text(label).FontSize(7).FontColor(Colors.Grey.Darken1);
                            c.Item().Text(value).FontSize(11).Bold();
                        });

                        Stat("Tổng sự cố", s.Total.ToString(CultureInfo.InvariantCulture));
                        Stat("Hoàn thành", s.Completed.ToString(CultureInfo.InvariantCulture));
                        Stat("Đã hủy", s.Cancelled.ToString(CultureInfo.InvariantCulture));
                        Stat("Phản ứng TB", s.AvgResponseMinutes.HasValue ? $"{Minutes(s.AvgResponseMinutes)} phút" : "—");
                        Stat("Xử lý TB", s.AvgResolutionMinutes.HasValue ? $"{Minutes(s.AvgResolutionMinutes)} phút" : "—");
                        Stat("Cấp 3 / 2 / 1 / Chưa PL",
                            $"{s.BySeverity.GetValueOrDefault("Level3")} / {s.BySeverity.GetValueOrDefault("Level2")} / {s.BySeverity.GetValueOrDefault("Level1")} / {s.BySeverity.GetValueOrDefault("Unclassified")}");
                    });
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(62);  // Báo cáo lúc
                        c.RelativeColumn(3);   // Tiêu đề / địa chỉ
                        c.ConstantColumn(48);  // Mức độ
                        c.ConstantColumn(52);  // Trạng thái
                        c.RelativeColumn(2);   // Trạm / xe
                        c.ConstantColumn(44);  // Phản ứng
                        c.ConstantColumn(44);  // Xử lý
                        c.RelativeColumn(2);   // Xác minh / đóng
                        c.RelativeColumn(3);   // Kết quả
                    });

                    table.Header(h =>
                    {
                        foreach (var title in new[] { "Báo cáo lúc", "Sự cố", "Mức độ", "Trạng thái", "Trạm / Xe", "Phản ứng (ph)", "Xử lý (ph)", "Xác minh / Đóng", "Kết quả" })
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(title).Bold();
                        }
                    });

                    foreach (var r in rows)
                    {
                        IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);

                        table.Cell().Element(Cell).Text(Local(r.ReportedAt));
                        table.Cell().Element(Cell).Column(c =>
                        {
                            c.Item().Text(r.Title).SemiBold();
                            c.Item().Text(r.LocationAddress).FontColor(Colors.Grey.Darken1);
                        });
                        table.Cell().Element(Cell).Text(SeverityLabel(r.Severity));
                        table.Cell().Element(Cell).Text(r.Status == IncidentStatus.Cancelled && r.CancellationReason.HasValue
                            ? $"{StatusLabel(r.Status)} ({CancelLabel(r.CancellationReason)})"
                            : StatusLabel(r.Status));
                        table.Cell().Element(Cell).Text($"{string.Join(", ", r.Stations)}\n{string.Join(", ", r.Units)}");
                        table.Cell().Element(Cell).AlignRight().Text(Minutes(r.ResponseMinutes));
                        table.Cell().Element(Cell).AlignRight().Text(Minutes(r.ResolutionMinutes));
                        table.Cell().Element(Cell).Text($"{r.VerifiedByName}\n{r.ClosedByName}");
                        table.Cell().Element(Cell).Text(r.ResolutionSummary ?? string.Empty);
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Trang ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
