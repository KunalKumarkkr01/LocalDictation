# ADR-0013: Enable a Claude co-author trailer for this repo

- **Status:** Accepted
- **Date:** 2026-07-04

## Context
The owner's global convention forbids AI-authorship markers on commits. For this specific project the
owner chose to opt in and credit the pairing.

## Decision
For **this repository only**, every commit (and PR body) ends with
`Co-Authored-By: Claude <noreply@anthropic.com>`. Commits are made in focused, phased chunks. This is
recorded in `CLAUDE.md`, which takes precedence over the global rule.

## Consequences
- This repo's history attributes the collaboration; other repos keep the global no-attribution rule.
