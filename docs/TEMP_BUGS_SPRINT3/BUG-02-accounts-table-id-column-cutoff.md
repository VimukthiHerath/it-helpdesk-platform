# BUG-02 — "All accounts" table: ID column looked cut off

**Severity:** Low (cosmetic).
**Status:** Fixed.
**Found:** Flagged by instructor during review.
**Area:** Admin — All Accounts.

## Summary

On the All Accounts page, the leftmost **ID** column sat flush against the
panel's edge with no breathing room, while every other column had a visible
gap before the next one — making the ID column look cut off / cramped
compared to the rest of the table.

## Steps to reproduce (before fix)

1. Log in as an admin.
2. Go to **All accounts**.
3. Look at the ID column on the left edge of the table.

## Expected

The ID column should have the same visual spacing as the rest of the table.

## Actual (before fix)

ID values sat directly against the left edge with no padding, visually
inconsistent with the rest of the row.

## Root cause

`frontend/src/features/admin/components/UserListPanel.css` — every table
cell only pads its **right** side:

```css
.user-table th { padding: 0 12px 10px 0; }
.user-table td { padding: 16px 12px 16px 0; }
```

Each column's right padding creates the visual gap before the *next*
column, which works fine for every column except the first one (ID) — there
is nothing padding its left, so it sits flush against the table/panel edge
while every other column gets a consistent 12px gap.

## Fix

Added matching left padding to the ID column specifically, in
`UserListPanel.css`:

```css
.user-table__id {
    width: 6%;
    padding-left: 12px;
}
```

Commit: see `fix(frontend):` in `git log` for the exact commit once
committed.

## Screenshot

_(Attach: before/after of the All Accounts table's left edge.)_
