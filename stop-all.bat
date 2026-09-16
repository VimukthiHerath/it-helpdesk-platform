@echo off
echo ============================================
echo Stopping IT Helpdesk Azure resources...
echo (MySQL is left running -- it's free under
echo  the Azure for Students grant. kafka-broker
echo  is being stopped too: expect a brief
echo  topic-recreation race the next time it and
echo  the app services come back up together)
echo ============================================
echo.

echo Stopping auth-service...
az containerapp update --name auth-service --resource-group it-helpdesk-rg --min-replicas 0

echo Stopping ticket-service...
az containerapp update --name ticket-service --resource-group it-helpdesk-rg --min-replicas 0

echo Stopping assignment-service...
az containerapp update --name assignment-service --resource-group it-helpdesk-rg --min-replicas 0

echo Stopping sla-service...
az containerapp update --name sla-service --resource-group it-helpdesk-rg --min-replicas 0

echo Stopping notification-service...
az containerapp update --name notification-service --resource-group it-helpdesk-rg --min-replicas 0

echo Stopping it-helpdesk-frontend...
az containerapp update --name it-helpdesk-frontend --resource-group it-helpdesk-rg --min-replicas 0

echo Stopping kafka-broker...
az containerapp update --name kafka-broker --resource-group it-helpdesk-rg --min-replicas 0

echo.
echo ============================================
echo Done. All 7 app services (including
echo kafka-broker) are stopped and not billing
echo at the active rate. MySQL keeps running as
echo usual -- nothing to do for it.
echo ============================================
pause
