package com.ithelpdesk;

import org.openqa.selenium.By;
import org.openqa.selenium.JavascriptExecutor;
import org.openqa.selenium.WebElement;
import org.openqa.selenium.support.ui.ExpectedConditions;
import org.testng.Assert;
import org.testng.annotations.Test;

/**
 * Covers the employee login form (frontend/src/features/auth/components/LoginForm.jsx).
 *
 * Prerequisites to run this suite:
 *   - Frontend dev server running at BASE_URL (default http://localhost:3000)
 *   - Auth.Api reachable at whatever REACT_APP_AUTH_API_URL the frontend was
 *     built/started with (the login form calls it directly, not through a gateway)
 *   - A registered employee account matching the credentials below - adjust
 *     TEST_EMAIL/TEST_PASSWORD to match a real account in your environment,
 *     e.g. one created via POST /api/auth/register.
 */
public class LoginSeleniumTest extends BaseTest {

    private static final String TEST_EMAIL = System.getProperty("testEmail", "demo-employee@example.com");
    private static final String TEST_PASSWORD = System.getProperty("testPassword", "DemoPass123!");

    @Test
    public void employeeCanLogInWithValidCredentials() {
        driver.get(BASE_URL + "/login");

        wait.until(ExpectedConditions.elementToBeClickable(By.id("email"))).sendKeys(TEST_EMAIL);
        driver.findElement(By.id("password")).sendKeys(TEST_PASSWORD);
        wait.until(ExpectedConditions.elementToBeClickable(By.cssSelector("button.auth-submit"))).click();

        // A successful login redirects away from /login (see LoginForm.jsx's
        // navigate('/', { replace: true }) on success).
        wait.until(ExpectedConditions.not(ExpectedConditions.urlContains("/login")));

        String token = (String) ((JavascriptExecutor) driver)
                .executeScript("return window.localStorage.getItem('token');");

        Assert.assertNotNull(token, "Expected a JWT to be stored in localStorage after login.");
        Assert.assertFalse(token.isEmpty(), "Expected the stored token to be non-empty.");
    }

    @Test
    public void invalidCredentialsShowErrorMessage() {
        driver.get(BASE_URL + "/login");

        wait.until(ExpectedConditions.elementToBeClickable(By.id("email"))).sendKeys(TEST_EMAIL);
        driver.findElement(By.id("password")).sendKeys("definitely-the-wrong-password");
        wait.until(ExpectedConditions.elementToBeClickable(By.cssSelector("button.auth-submit"))).click();

        // Login failure shows a react-toastify error toast (see LoginForm.jsx's
        // toast.error(...) in the catch block) and never navigates away. The
        // message text is a direct child of .Toastify__toast--error, not
        // wrapped in .Toastify__toast-body (that class exists in the CSS but
        // isn't used for a plain-string toast with the default icon).
        WebElement toast = wait.until(ExpectedConditions.visibilityOfElementLocated(
                By.cssSelector(".Toastify__toast--error[role='alert']")));

        Assert.assertTrue(toast.getText().contains("Invalid email or password"),
                "Expected the 'Invalid email or password.' error message, got: " + toast.getText());
        Assert.assertTrue(driver.getCurrentUrl().contains("/login"),
                "A failed login should not navigate away from the login page.");
    }

    @Test
    public void emptyFormShowsFieldValidationWithoutHittingTheApi() {
        driver.get(BASE_URL + "/login");

        // Submit with both fields empty - LoginForm.jsx's own validateForm()
        // should block the request before it ever reaches Auth.Api.
        wait.until(ExpectedConditions.elementToBeClickable(By.cssSelector("button.auth-submit"))).click();

        WebElement emailError = wait.until(ExpectedConditions.visibilityOfElementLocated(
                By.cssSelector("#email")))
                .findElement(By.xpath("following-sibling::span[@class='error-text']"));

        Assert.assertEquals(emailError.getText(), "Email is required");
        Assert.assertTrue(driver.getCurrentUrl().contains("/login"),
                "Client-side validation should keep the user on the login page.");
    }
}
