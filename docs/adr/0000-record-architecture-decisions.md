# 0000. Record architecture decisions

Date: 2026-11-02 · Status: Accepted

## Context

This project is developed in short, independent sessions, mostly by AI agents that
start with no memory of earlier sessions. The reasons behind past choices have to be
on disk, or each session will rediscover or quietly reverse them.

## Decision

Record significant decisions as Architecture Decision Records in `docs/adr/`, one
Markdown file per decision, named `NNNN-short-title.md` and numbered in sequence.
Each one has Context, Decision, and Consequences sections, plus a date and a status
(Proposed, Accepted, Superseded by NNNN, or Rejected).

Accepted ADRs are not rewritten. To change a decision, write a new ADR that
supersedes the old one, and update the old one's status line only.

## Consequences

- A worker session checks `docs/adr/` before proposing a change that contradicts an
  earlier decision.
- Writing an ADR costs a little time; that cost is the point, since it forces a stated
  reason. Small, easily reversed choices don't need one.
