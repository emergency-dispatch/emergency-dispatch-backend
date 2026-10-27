using EmergencyDispatch.Application.DTOs.Incident;
using FluentValidation;

namespace EmergencyDispatch.Application.Validators;

public class CreateIncidentDtoValidator : AbstractValidator<CreateIncidentDto>
{
    public CreateIncidentDtoValidator()
    {
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90.0, 90.0)
            .WithMessage("Vĩ độ (Latitude) phải nằm trong khoảng -90 đến 90 độ.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180.0, 180.0)
            .WithMessage("Kinh độ (Longitude) phải nằm trong khoảng -180 đến 180 độ.");

        RuleFor(x => x.Description)
            .MaximumLength(200).WithMessage("Mô tả sự việc không được vượt quá 200 ký tự.");

        RuleFor(x => x.LocationAddress)
            .NotEmpty().WithMessage("Địa chỉ hiện trường là bắt buộc.")
            .MaximumLength(500).WithMessage("Địa chỉ không được vượt quá 500 ký tự.");

        When(x => !string.IsNullOrEmpty(x.ReporterPhone), () =>
        {
            RuleFor(x => x.ReporterPhone)
                .Matches(@"^(0|\+84)[35789][0-9]{8}$")
                .WithMessage("Số điện thoại liên hệ không đúng định dạng Việt Nam.");
        });

        RuleFor(x => x.MediaUrls)
            .Must(urls => urls == null || urls.Count <= 5)
            .WithMessage("Không được đính kèm quá 5 tệp tin phương tiện cho một sự cố.");
    }
}
