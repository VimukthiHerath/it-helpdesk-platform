Write-Host "Starting IT Helpdesk Platform Backend Services..." -ForegroundColor Green

$dotnetPath = "C:\Program Files\dotnet\dotnet.exe"

Write-Host "Starting Auth API on port 5121..."
Start-Process cmd -ArgumentList "/k cd services\auth\Auth.Api && `"$dotnetPath`" run --urls http://localhost:5121"

Write-Host "Starting Ticket API on port 5164..."
Start-Process cmd -ArgumentList "/k cd services\ticket\Ticket.Api && `"$dotnetPath`" run --urls http://localhost:5164"

Write-Host "Starting Assignment API on port 5067..."
Start-Process cmd -ArgumentList "/k cd services\assignment\Assignment.Api && `"$dotnetPath`" run --urls http://localhost:5067"

Write-Host "Starting Notification API on port 5214..."
Start-Process cmd -ArgumentList "/k cd services\notification\Notification.Api && `"$dotnetPath`" run --urls http://localhost:5214"

Write-Host "Starting SLA API on port 5262..."
Start-Process cmd -ArgumentList "/k cd services\sla\Sla.Api && `"$dotnetPath`" run --urls http://localhost:5262"

Write-Host "All backend microservices have been launched in separate Command Prompt windows!" -ForegroundColor Green
Write-Host "You can now run 'npm start' in the frontend folder."
