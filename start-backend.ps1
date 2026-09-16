Write-Host "Starting IT Helpdesk Platform Backend Services..." -ForegroundColor Green

$dotnetPath = "C:\Program Files\dotnet\dotnet.exe"

Write-Host "Starting Auth API on port 5001..."
Start-Process cmd -ArgumentList "/k cd services\auth\Auth.Api && `"$dotnetPath`" run --urls http://localhost:5001"

Write-Host "Starting Ticket API on port 5002..."
Start-Process cmd -ArgumentList "/k cd services\ticket\Ticket.Api && `"$dotnetPath`" run --urls http://localhost:5002"

Write-Host "Starting Assignment API on port 5003..."
Start-Process cmd -ArgumentList "/k cd services\assignment\Assignment.Api && `"$dotnetPath`" run --urls http://localhost:5003"

Write-Host "Starting Notification API on port 5004..."
Start-Process cmd -ArgumentList "/k cd services\notification\Notification.Api && `"$dotnetPath`" run --urls http://localhost:5004"

Write-Host "Starting SLA API on port 5005..."
Start-Process cmd -ArgumentList "/k cd services\sla\Sla.Api && `"$dotnetPath`" run --urls http://localhost:5005"

Write-Host "All backend microservices have been launched in separate Command Prompt windows!" -ForegroundColor Green
Write-Host "You can now run 'npm start' in the frontend folder."
