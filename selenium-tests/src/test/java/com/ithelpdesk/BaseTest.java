package com.ithelpdesk;

import io.github.bonigarcia.wdm.WebDriverManager;
import org.openqa.selenium.OutputType;
import org.openqa.selenium.TakesScreenshot;
import org.openqa.selenium.WebDriver;
import org.openqa.selenium.chrome.ChromeDriver;
import org.openqa.selenium.chrome.ChromeOptions;
import org.openqa.selenium.logging.LogType;
import org.openqa.selenium.logging.LoggingPreferences;
import org.openqa.selenium.support.ui.WebDriverWait;
import org.testng.ITestResult;
import org.testng.annotations.AfterMethod;
import org.testng.annotations.BeforeMethod;

import java.io.File;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;

/**
 * Shared WebDriver lifecycle for all Selenium tests. Each test method gets
 * its own fresh browser instance (BeforeMethod/AfterMethod, not BeforeClass)
 * so one test's leftover state (localStorage token, cookies, open dialogs)
 * can never bleed into the next test.
 */
public abstract class BaseTest {

    // Override with -DbaseUrl=... (see pom.xml) to point at a different
    // environment without touching test code.
    protected static final String BASE_URL = System.getProperty("baseUrl", "http://localhost:3000");

    protected WebDriver driver;
    protected WebDriverWait wait;

    @BeforeMethod
    public void setUp() {
        WebDriverManager.chromedriver().setup();

        ChromeOptions options = new ChromeOptions();
        // Run with -Dheadless=true for CI; defaults to a visible browser
        // locally so failures are easy to actually watch happen.
        if (Boolean.getBoolean("headless")) {
            options.addArguments("--headless=new");
        }
        options.addArguments("--window-size=1440,900");

        LoggingPreferences logPrefs = new LoggingPreferences();
        logPrefs.enable(LogType.BROWSER, java.util.logging.Level.ALL);
        options.setCapability("goog:loggingPrefs", logPrefs);

        driver = new ChromeDriver(options);
        wait = new WebDriverWait(driver, Duration.ofSeconds(10));
    }

    // Auto-captures a screenshot for any failed test, saved under
    // target/screenshots/<TestClass>.<testMethod>.png - real, ready-to-attach
    // evidence for the report without having to reproduce the failure by hand.
    @AfterMethod
    public void tearDown(ITestResult result) {
        if (driver != null) {
            if (!result.isSuccess()) {
                try {
                    Path screenshotsDir = Path.of("target", "screenshots");
                    Files.createDirectories(screenshotsDir);
                    File src = ((TakesScreenshot) driver).getScreenshotAs(OutputType.FILE);
                    String name = result.getTestClass().getRealClass().getSimpleName()
                            + "." + result.getMethod().getMethodName() + ".png";
                    Files.copy(src.toPath(), screenshotsDir.resolve(name),
                            java.nio.file.StandardCopyOption.REPLACE_EXISTING);
                    System.out.println("Saved failure screenshot: " + screenshotsDir.resolve(name));
                } catch (IOException e) {
                    System.err.println("Could not save failure screenshot: " + e.getMessage());
                }
            }
            driver.quit();
        }
    }
}
