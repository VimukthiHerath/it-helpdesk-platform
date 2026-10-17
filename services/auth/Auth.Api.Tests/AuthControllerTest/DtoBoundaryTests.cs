using System.ComponentModel.DataAnnotations;
using Auth.Api.DTO;
using Xunit;

namespace Auth.Api.Tests;

public class DtoBoundaryTests
{
    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void UserRegister_NameLengthBoundary_IsValidated(int length, bool valid) =>
        Assert.Equal(valid, Validate(new UserRegisterDTO
        {
            Name = new string('n', length),
            Email = "user@example.com",
            Password = "secret"
        }).Count == 0);

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public void UserRegister_PasswordLengthBoundary_IsValidated(int length, bool valid) =>
        Assert.Equal(valid, Validate(new UserRegisterDTO
        {
            Name = "User",
            Email = "user@example.com",
            Password = new string('p', length)
        }).Count == 0);

    [Fact]
    public void UserRegister_EmailMustBeNonEmptyAndValid()
    {
        Assert.NotEmpty(Validate(new UserRegisterDTO { Name = "User", Password = "secret" }));
        Assert.NotEmpty(Validate(new UserRegisterDTO
        {
            Name = "User",
            Email = "not-an-email",
            Password = "secret"
        }));
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public void UserLogin_EmailLengthBoundary_IsValidated(int length, bool valid)
    {
        var results = Validate(new UserLoginDTO
        {
            Email = new string('a', length - 12) + "@example.com",
            Password = "secret"
        });

        Assert.Equal(valid, results.Count == 0);
    }

    private static IList<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, true);
        return results;
    }
}
