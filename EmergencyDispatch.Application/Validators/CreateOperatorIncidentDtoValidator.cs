using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Configuration;

namespace EmergencyDispatch.Application.Validators;

public class CreateOperatorIncidentDtoValidator : AbstractValidator<CreateOperatorIncidentDto>
{
    public const int MinNotesLength = 5;

    public CreateOperatorIncidentDtoValidator(IConfiguration configuration)
    {
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90.0, 90.0)
            .WithMessage("Vĩ độ (Latitude) phải nằm trong khoảng -90 đến 90 độ.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180.0, 180.0)
            .WithMessage("Kinh độ (Longitude) phải nằm trong khoảng -180 đến 180 độ.");

        RuleFor(x => x.LocationAddress)
            .NotEmpty().WithMessage("Địa chỉ hiện trường là bắt buộc.")
            .MaximumLength(500).WithMessage("Địa chỉ không được vượt quá 500 ký tự.");

        RuleFor(x => x.Title)
            .MaximumLength(300).WithMessage("Tiêu đề không được vượt quá 300 ký tự.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Mô tả không được vượt quá 1000 ký tự.");

        RuleFor(x => x.Source)
            .Must(s => s != IncidentSource.CitizenApp && Enum.IsDefined(s))
            .WithMessage("Chọn kênh biết tin: PhoneCall, Radio, Cctv, FieldObservation hoặc Other.");

        RuleFor(x => x.EventType)
            .Must(t => t is IncidentEventType.FireExplosion or IncidentEventType.TrafficAccident)
            .WithMessage("Chọn loại sự cố: FireExplosion (cháy nổ) hoặc TrafficAccident (tai nạn).");

        RuleFor(x => x.Severity)
            .Must(s => s is SeverityLevel.Level1 or SeverityLevel.Level2 or SeverityLevel.Level3)
            .WithMessage("Mức độ nghiêm trọng phải từ 1 đến 3.");

        RuleFor(x => x.OperatorNotes)
            .Must(n => (n?.Trim().Length ?? 0) >= MinNotesLength)
            .WithMessage("Vui lòng ghi nội dung tin báo / căn cứ đánh giá (tối thiểu 5 ký tự) vào operatorNotes.");

        When(x => !string.IsNullOrEmpty(x.ReporterPhone), () =>
        {
            RuleFor(x => x.ReporterPhone)
                .Matches(@"^(0|\+84)[35789][0-9]{8}$")
                .WithMessage("Số điện thoại liên hệ không đúng định dạng Việt Nam.");
        });

        RuleFor(x => x.MediaUrls)
            .Must(urls => urls == null || urls.Count <= 5)
            .WithMessage("Không được đính kèm quá 5 tệp tin phương tiện cho một sự cố.");

        RuleForEach(x => x.MediaUrls)
            .Must(url => MediaUrlPolicy.IsTrusted(url, configuration))
            .WithMessage("Ảnh / video phải được tải lên qua hệ thống (POST /api/media/upload).");
    }
}
