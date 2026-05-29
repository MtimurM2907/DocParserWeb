using System.ComponentModel.DataAnnotations;
using DocParseLab.Server.DTOs;

namespace DocParseLab.Tests;

public class DtoValidationTests
{
    [Fact]
    public void AuthRequest_InvalidEmail_FailsValidation()
    {
        var dto = new AuthRequest
        {
            Email = "not-an-email",
            Password = "123456",
        };

        var errors = Validate(dto);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(AuthRequest.Email)));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("docx")]
    public void SendDocumentEmailRequest_AllowedFormat_PassesValidation(string format)
    {
        var dto = new SendDocumentEmailRequest
        {
            TargetEmail = "user@example.com",
            Format = format,
        };

        var errors = Validate(dto);

        Assert.Empty(errors);
    }

    [Fact]
    public void SendDocumentEmailRequest_InvalidFormat_FailsValidation()
    {
        var dto = new SendDocumentEmailRequest
        {
            TargetEmail = "user@example.com",
            Format = "exe",
        };

        var errors = Validate(dto);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(SendDocumentEmailRequest.Format)));
    }

    private static List<ValidationResult> Validate(object dto)
    {
        var context = new ValidationContext(dto);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, context, results, validateAllProperties: true);
        return results;
    }
}
