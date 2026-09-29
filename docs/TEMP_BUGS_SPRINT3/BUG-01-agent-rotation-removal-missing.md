# BUG-01 — No way to remove an agent from the ticket rotation

**Severity:** High — blocks a real admin workflow with no UI workaround.
**Status:** Open (not fixed).
**Found:** Manual testing, Sprint 3 QA pass.
**Area:** Admin — All Accounts / Agent Rotation.

## Summary

An admin can add an agent to the ticket rotation, but there is no way to
remove one — not from the Agent Rotation page, and not from the Deactivate
button on the All Accounts page. Once an agent has ever been added to the
rotation, their account can never be deactivated or have its role changed
again through the app.

## Steps to reproduce

1. Log in as an admin.
2. Go to **Agent rotation**, add an agent.
3. Go to **All accounts**.
4. Click **Deactivate** (or **Edit** → change role) on that same agent.

## Expected

Either the deactivation/role-change succeeds, or there's a clear path to
remove the agent from the rotation first and then retry.

## Actual

The action is blocked with:

> "This agent is still in the ticket rotation. Remove them from the rotation
> before changing their role or deactivating this account."

There is no button, page, or API endpoint anywhere in the app that performs
that removal. The instruction in the error message refers to an action that
doesn't exist.

## Root cause

- **Backend** (`services/auth/Auth.Api/Controller/AuthController.cs:350-367`,
  `BlockIfAgentStillInRotationAsync`) correctly checks rotation status via
  Assignment.Api and blocks with the message above — this part works as
  designed.
- **Assignment.Api** (`services/assignment/Assignment.Api/Controller/AssignmentsController.cs`)
  only exposes `GET /api/assignments/agents` (list) and
  `POST /api/assignments/agents` (add). There is no `DELETE` or equivalent
  removal endpoint.
- **Frontend** (`frontend/src/features/admin/components/AgentRotationPanel.jsx`,
  lines 126-134) renders the rotation list as read-only — each entry just
  shows the user ID and slot number, no remove button.

So the block logic (add-side) shipped, but the removal feature it depends on
was never built on either the backend or the frontend.

## Workaround (for continued testing only)

Remove the row directly from the `assignment_db.Agents` table. Not something
an admin can do through the app.

## Suggested fix

- `Assignment.Api`: add `DELETE /api/assignments/agents/{userId}` (or similar).
- `AgentRotationPanel.jsx`: add a Remove button per row calling it.

## Screenshot

_(Attach: the "All accounts" row showing the block message on Deactivate.)_
