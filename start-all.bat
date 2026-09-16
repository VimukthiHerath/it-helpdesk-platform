@echo off
echo ============================================
echo Starting IT Helpdesk Azure resources...
echo (MySQL was never stopped. kafka-broker is
echo  starting fresh too -- give it a moment
echo  before the app services start producing/
echo  consuming, or you may hit a brief
echo  topic-doesn't-exist race)
echo ============================================
echo.

echo Starting kafka-broker...
az containerapp update --name kafka-broker --resource-group it-helpdesk-rg --min-replicas 1 --max-replicas 1

echo Starting auth-service...
az containerapp update --name auth-service --resource-group it-helpdesk-rg --min-replicas 1 --max-replicas 1

echo Starting ticket-service...
az containerapp update --name ticket-service --resource-group it-helpdesk-rg --min-replicas 1 --max-replicas 1

echo Starting assignment-service...
az containerapp update --name assignment-service --resource-group it-helpdesk-rg --min-replicas 1 --max-replicas 1

echo Starting sla-service...
az containerapp update --name sla-service --resource-group it-helpdesk-rg --min-replicas 1 --max-replicas 1

echo Starting notification-service...
az containerapp update --name notification-service --resource-group it-helpdesk-rg --min-replicas 1 --max-replicas 1

echo Starting it-helpdesk-frontend...
az containerapp update --name it-helpdesk-frontend --resource-group it-helpdesk-rg --min-replicas 1 --max-replicas 1

echo.
echo ============================================
echo Done. Services are starting up, kafka-broker
echo first so the others have a broker to talk to.
echo Give it a few seconds before testing.
echo ============================================
pause
