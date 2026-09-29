package com.ithelpdesk;

import org.openqa.selenium.By;
import org.openqa.selenium.JavascriptExecutor;
import org.openqa.selenium.WebElement;
import org.openqa.selenium.support.ui.ExpectedConditions;
import org.testng.Assert;
import org.testng.annotations.Test;

import java.util.List;

/**
 * Covers REPORT-1: "As an administrator, I want a dynamic report of tickets
 * filterable by status, urgency, and date range" (frontend/src/pages/adminReport.jsx).
 *
 * Prerequisites: an admin account matching ADMIN_EMAIL/ADMIN_PASSWORD, and at
 * least one ticket in the system for the "results found" tests.
 */
public class TicketReportSeleniumTest extends BaseTest {

    private static final String ADMIN_EMAIL = System.getProperty("adminEmail", "admin@example.com");
    private static final String ADMIN_PASSWORD = System.getProperty("adminPassword", "Admin123!");

    // AC1 + AC2: only an admin can reach it, and the results table shows the
    // required columns once filters are applied.
    @Test
    public void adminCanViewTicketReport() {
        AuthHelper.loginAs(driver, BASE_URL, ADMIN_EMAIL, ADMIN_PASSWORD, "/admin/report");

        wait.until(ExpectedConditions.elementToBeClickable(By.id("report-apply-btn"))).click();

        WebElement table = wait.until(ExpectedConditions.visibilityOfElementLocated(
                By.cssSelector(".report-table")));

        // Compared case-insensitively: the design system CSS applies
        // text-transform: uppercase to headers, which getText() reflects as
        // rendered text - that's a styling choice, not what AC2 cares about.
        List<String> headers = table.findElements(By.cssSelector("thead th"))
                .stream().map(el -> el.getText().toUpperCase()).toList();

        Assert.assertEquals(headers,
                List.of("Ticket ID", "Subject", "Status", "Urgency", "Created Date", "Assigned Agent")
                        .stream().map(String::toUpperCase).toList(),
                "Report table should show exactly the columns REPORT-1 AC2 requires.");

        int rowCount = table.findElements(By.cssSelector("tbody tr")).size();
        Assert.assertTrue(rowCount > 0, "Expected at least one ticket in an unfiltered report.");
    }

    // AC3: filtering by status returns only matching rows.
    @Test
    public void filteringByStatusReturnsOnlyMatchingTickets() {
        AuthHelper.loginAs(driver, BASE_URL, ADMIN_EMAIL, ADMIN_PASSWORD, "/admin/report");

        new org.openqa.selenium.support.ui.Select(driver.findElement(By.id("report-status")))
                .selectByValue("2"); // Resolved
        wait.until(ExpectedConditions.elementToBeClickable(By.id("report-apply-btn"))).click();

        WebElement table = wait.until(ExpectedConditions.visibilityOfElementLocated(
                By.cssSelector(".report-table")));
        List<WebElement> statusCells = table.findElements(By.cssSelector("tbody tr td:nth-child(3)"));

        Assert.assertFalse(statusCells.isEmpty(), "Expected at least one Resolved ticket to test against.");
        for (WebElement cell : statusCells) {
            // Case-insensitive - see adminCanViewTicketReport's comment on
            // the uppercase text-transform styling.
            Assert.assertEquals(cell.getText().trim().toUpperCase(), "RESOLVED",
                    "Every row should be Resolved when filtered by status=Resolved.");
        }
    }

    // AC4: "No tickets found" message shown when no matches.
    @Test
    public void noMatchesShowsNoTicketsFoundMessage() {
        AuthHelper.loginAs(driver, BASE_URL, ADMIN_EMAIL, ADMIN_PASSWORD, "/admin/report");

        // A start date far in the future guarantees zero matches. Two things
        // make this trickier than a plain sendKeys/JS value assignment:
        //   1. <input type=date>'s value must be ISO format (yyyy-MM-dd) -
        //      sendKeys would depend on whatever date format Chrome's locale
        //      expects for keystrokes, which isn't reliable across machines.
        //   2. React tracks input values through a patched native setter, so
        //      a plain `element.value = x` bypasses React's change detection
        //      entirely and no onChange fires. Using the *native* HTMLInputElement
        //      setter (rather than the instance's own, which React has
        //      overridden) is what actually makes React notice the change -
        //      a well-known Selenium+React gotcha, not a workaround specific
        //      to this app.
        WebElement startDate = driver.findElement(By.id("report-startDate"));
        ((JavascriptExecutor) driver).executeScript(
                "const nativeSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;"
                        + "nativeSetter.call(arguments[0], arguments[1]);"
                        + "arguments[0].dispatchEvent(new Event('input', { bubbles: true }));"
                        + "arguments[0].dispatchEvent(new Event('change', { bubbles: true }));",
                startDate, "2099-12-31");

        wait.until(ExpectedConditions.elementToBeClickable(By.id("report-apply-btn"))).click();

        WebElement message = wait.until(ExpectedConditions.visibilityOfElementLocated(
                By.cssSelector(".report-message strong")));

        Assert.assertEquals(message.getText(), "No tickets found");
    }
}
