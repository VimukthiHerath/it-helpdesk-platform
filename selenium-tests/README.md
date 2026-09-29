# Selenium UI Tests

Browser-driven end-to-end tests for the IT Helpdesk frontend. Java + Maven +
TestNG + Selenium WebDriver, matching the convention from the Sprint 2 QA
report (`docs/QA_report_IT24101503_Group02.pdf`).

## Prerequisites

- Java 17+ and Maven (already required by this project)
- Google Chrome installed — ChromeDriver itself is handled automatically by
  WebDriverManager at test run time, no manual driver download needed
- The frontend running at `http://localhost:3000` (`npm start` in `frontend/`)
- Auth.Api running and reachable at whatever `REACT_APP_AUTH_API_URL` the
  frontend was started with — the login form calls it directly, not through
  either gateway
- A real employee account to log in with (adjust `-DtestEmail`/
  `-DtestPassword` below to match one in your environment)

## Running

```bash
cd selenium-tests
mvn test
```

Useful overrides:

```bash
# Point at a different environment
mvn test -DbaseUrl=https://staging.example.com

# Use a different test account
mvn test -DtestEmail=someone@example.com -DtestPassword=secret

# Run headless (e.g. in CI)
mvn test -Dheadless=true

# Run a single test method
mvn test -Dtest=LoginSeleniumTest#employeeCanLogInWithValidCredentials
```

## Structure

```
selenium-tests/
├── pom.xml
├── src/test/java/com/ithelpdesk/
│   ├── BaseTest.java          # WebDriver lifecycle - one fresh browser per test method
│   └── LoginSeleniumTest.java # first suite: employee login
└── src/test/resources/
    └── testng.xml             # test suite definition
```

Each new feature area gets its own `<Feature>SeleniumTest.java` class under
`com.ithelpdesk`, added to `testng.xml`, following the same pattern as
`LoginSeleniumTest`.

## Failure screenshots

`BaseTest` automatically saves a screenshot for any failed test to
`target/screenshots/<TestClass>.<testMethod>.png` — real, ready-to-attach
evidence for the report without having to reproduce the failure by hand.

## A note on debugging failures

Don't assume a failing assertion means the app is broken — check what
actually happened first. While writing `LoginSeleniumTest`, an assertion
failed because of a wrong CSS selector guess (`.Toastify__toast-body`,
which exists in react-toastify's CSS but isn't actually used in the
rendered DOM for a plain-string toast). The failure screenshot plus the
browser's own console log (enabled via `goog:loggingPrefs` in
`BaseTest.setUp()`) made it obvious the app was working correctly — the
error toast had rendered with the right message — and the test's locator
was wrong, not the app.
