package com.ithelpdesk;

import org.openqa.selenium.JavascriptExecutor;
import org.openqa.selenium.WebDriver;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Logs in via a direct HTTP call to Auth.Api and injects the resulting JWT
 * straight into the browser's localStorage, instead of driving the login
 * form through Selenium every time. Tests that aren't specifically about the
 * login flow itself (e.g. the report page tests) shouldn't have their
 * pass/fail depend on the login UI too - that's LoginSeleniumTest's job.
 */
final class AuthHelper {

    private static final String AUTH_API_URL = System.getProperty("authApiUrl", "http://localhost:5121");
    private static final Pattern TOKEN_PATTERN = Pattern.compile("\"token\"\\s*:\\s*\"([^\"]+)\"");

    private AuthHelper() {
    }

    static String fetchToken(String email, String password) {
        try {
            HttpClient client = HttpClient.newHttpClient();
            String body = String.format("{\"email\":\"%s\",\"password\":\"%s\"}", email, password);
            HttpRequest request = HttpRequest.newBuilder()
                    .uri(URI.create(AUTH_API_URL + "/api/auth/login"))
                    .header("Content-Type", "application/json")
                    .POST(HttpRequest.BodyPublishers.ofString(body))
                    .build();

            HttpResponse<String> response = client.send(request, HttpResponse.BodyHandlers.ofString());
            if (response.statusCode() != 200) {
                throw new RuntimeException("Login failed for " + email + ": HTTP " + response.statusCode()
                        + " - " + response.body());
            }

            Matcher matcher = TOKEN_PATTERN.matcher(response.body());
            if (!matcher.find()) {
                throw new RuntimeException("No token found in login response: " + response.body());
            }
            return matcher.group(1);
        } catch (IOException | InterruptedException e) {
            throw new RuntimeException("Could not reach Auth.Api at " + AUTH_API_URL, e);
        }
    }

    /**
     * Navigates to baseUrl (so localStorage is on the right origin), injects
     * the token, then navigates to the actual target page.
     */
    static void loginAs(WebDriver driver, String baseUrl, String email, String password, String targetPath) {
        String token = fetchToken(email, password);
        driver.get(baseUrl);
        ((JavascriptExecutor) driver).executeScript("window.localStorage.setItem('token', arguments[0]);", token);
        driver.get(baseUrl + targetPath);
    }
}
