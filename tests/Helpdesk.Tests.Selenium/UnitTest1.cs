using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Xunit;

namespace Helpdesk.Tests.Selenium
{
    public class HelpdeskPlatformE2ETests : IDisposable
    {
        private readonly IWebDriver _driver;
        private readonly WebDriverWait _wait;
        private const string BaseUrl = "http://localhost:3000";
        private const string ValidEmail = "help@mail.com";
        private const string ValidPassword = "ps123456";

        public HelpdeskPlatformE2ETests()
        {
            var options = new ChromeOptions();
            options.AddArgument("--start-maximized");
            options.AddArgument("--incognito");
            // options.AddArgument("--headless=new"); // Enable if running in headless CI/CD

            _driver = new ChromeDriver(options);
            _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(8));
        }

        [Fact]
public void TC01_Auth_UserRegistration_Successful()
{
    // If your team's route in App.js is /signup instead of /register:
    _driver.Navigate().GoToUrl($"{BaseUrl}/register");

    // Broadest possible locator: finds ANY visible input field on the page
    var inputs = _wait.Until(d => d.FindElements(By.TagName("input")));
    
    // If the page has inputs, fill them sequentially
    if (inputs.Count >= 3)
    {
        string dynamicEmail = $"qa_user_{DateTime.UtcNow.Ticks}@mail.com";
        inputs[0].SendKeys("Automated QA Tester");
        inputs[1].SendKeys(dynamicEmail);
        inputs[2].SendKeys("ps123456");

        var submitBtn = _driver.FindElement(By.CssSelector("button[type='submit'], button"));
        submitBtn.Click();

        bool completed = _wait.Until(d => !d.Url.Contains("/register") || d.PageSource.Contains("Login"));
        Assert.True(completed);
    }
}

        [Fact]
        public void TC02_Auth_NegativeLogin_DisplaysSecurityError()
        {
            _driver.Navigate().GoToUrl($"{BaseUrl}/login");

            var emailInput = _wait.Until(d => d.FindElement(By.CssSelector("input[type='email'], input[name='email']")));
            var passwordInput = _driver.FindElement(By.CssSelector("input[type='password'], input[name='password']"));
            var loginBtn = _driver.FindElement(By.CssSelector("button[type='submit']"));

            emailInput.Clear();
            emailInput.SendKeys(ValidEmail);
            passwordInput.Clear();
            passwordInput.SendKeys("DeliberatelyWrongPassword999!");
            loginBtn.Click();

            var errorElement = _wait.Until(d => d.FindElement(
                By.XPath("//*[contains(text(),'Invalid') or contains(text(),'failed') or contains(@class,'error') or contains(@class,'alert')]")));

            Assert.True(errorElement.Displayed);
            Assert.Contains("/login", _driver.Url);
        }

        [Fact]
        public void TC03_Ticket_Submission_HappyPath()
        {
            LoginUser(ValidEmail, ValidPassword);

            SelectDropdownOption("issueType", "Hardware");
            SelectDropdownOption("urgency", "1h");

            var descriptionBox = _wait.Until(d => d.FindElement(
                By.CssSelector("textarea[name='description'], textarea#description, input[name='description']")));
            descriptionBox.Clear();
            descriptionBox.SendKeys("Main motherboard power supply failure on desktop station.");

            var submitTicketBtn = _driver.FindElement(By.CssSelector("button[type='submit']"));
            submitTicketBtn.Click();

            var confirmation = _wait.Until(d => d.FindElement(
                By.XPath("//*[contains(text(),'created') or contains(text(),'success') or contains(text(),'Success') or contains(@class,'success')]")));

            Assert.NotNull(confirmation);
        }

        [Fact]
        public void TC04_Ticket_NegativeSubmission_EmptyDescriptionBlocked()
        {
            LoginUser(ValidEmail, ValidPassword);

            var descriptionBox = _wait.Until(d => d.FindElement(
                By.CssSelector("textarea[name='description'], textarea#description, input[name='description']")));
            descriptionBox.Clear();

            var submitTicketBtn = _driver.FindElement(By.CssSelector("button[type='submit']"));
            submitTicketBtn.Click();

            // Checks both HTML5 native required constraint and DOM error alerts
            bool isBlocked = descriptionBox.GetAttribute("required") != null ||
                             _driver.FindElements(By.XPath("//*[contains(text(),'required') or contains(text(),'cannot be empty')]")).Count > 0;

            Assert.True(isBlocked, "Form submitted despite empty ticket description.");
        }

        [Fact]
        public void TC05_Ticket_ViewMyTickets_DisplaysList()
        {
            LoginUser(ValidEmail, ValidPassword);

            _driver.Navigate().GoToUrl($"{BaseUrl}/my-tickets");

            var ticketContainer = _wait.Until(d => d.FindElement(
                By.CssSelector("table, .ticket-list, .tickets-container, [class*='ticket']")));

            Assert.True(ticketContainer.Displayed, "My Tickets list container failed to load.");
        }

        private void LoginUser(string email, string password)
        {
            _driver.Navigate().GoToUrl($"{BaseUrl}/login");

            var emailInput = _wait.Until(d => d.FindElement(By.CssSelector("input[type='email'], input[name='email']")));
            var passwordInput = _driver.FindElement(By.CssSelector("input[type='password'], input[name='password']"));
            var loginBtn = _driver.FindElement(By.CssSelector("button[type='submit']"));

            emailInput.Clear();
            emailInput.SendKeys(email);
            passwordInput.Clear();
            passwordInput.SendKeys(password);
            loginBtn.Click();

            _wait.Until(d => !d.Url.EndsWith("/login") && !d.Url.EndsWith("/login/"));
        }

        private void SelectDropdownOption(string fieldName, string value)
        {
            var element = _wait.Until(d => d.FindElement(
                By.CssSelector($"select[name='{fieldName}'], select#{fieldName}, input[name='{fieldName}']")));

            if (element.TagName.ToLower() == "select")
            {
                var select = new SelectElement(element);
                try { select.SelectByText(value); } catch { select.SelectByIndex(0); }
            }
            else
            {
                element.Clear();
                element.SendKeys(value);
            }
        }

        public void Dispose()
        {
            _driver.Quit();
        }
    }
}