#!/usr/bin/env bash
# Unit tests for the Ticket service, with coverage. Owner: Shamim.
#
# Needs a MySQL on localhost:3307 with the ticket_db schema, because some of the existing tests start
# the real application. Locally:
#   docker compose up -d mysql
#   (cd services/ticket/Ticket.Api && dotnet ef database update --context ApplicationDbContext)
# Results land in TestResults/unit/ (a .trx file and an opencover coverage file).
set -u
cd "$(dirname "$0")/../.."

OUT=TestResults/unit
rm -rf "$OUT"

# In the pipeline the connection string comes from the environment; locally it falls back to the
# local Docker MySQL.
export ConnectionStrings__TicketDb="${ConnectionStrings__TicketDb:-Server=127.0.0.1;Port=3307;Database=ticket_db;Uid=root;Pwd=localdevpassword;}"
export TZ=UTC

dotnet test services/ticket/Ticket.Api.Tests/Ticket.Api.Tests.csproj \
  --configuration Release \
  --filter "Category!=Integration" \
  --logger "trx;LogFileName=ticket-unit.trx" \
  --results-directory "$OUT" \
  -p:CollectCoverage=true -p:CoverletOutputFormat=opencover \
  -p:CoverletOutput="../../../$OUT/ticket-unit-coverage"
