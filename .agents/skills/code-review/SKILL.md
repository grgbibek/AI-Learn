---
name: code-review
description: Perform automated, comprehensive code review for .NET 10 API and Angular 19 codebases. Audits architectural alignment, type safety, security, performance, and memory leaks.
---

# Code Review Agent Skill

This skill defines the instructions and checklists for performing automated code reviews on full-stack **.NET 10** and **Angular 19** applications.

## Review Audit Checklist

### 1. Backend Audit (.NET 10 / C#)
- **Minimal API Contracts**: Ensure endpoints return explicit `TypedResults` or proper HTTP status codes.
- **DTO Immutability**: Verify requests/responses use `record` or `readonly` structures with C# primary constructors.
- **Async & DB Best Practices**: Confirm all EF Core calls use `ToListAsync()`, `FirstOrDefaultAsync()`, and proper `CancellationToken` passing where applicable. No blocking `.Result` or `.Wait()`.
- **CORS & Security**: Verify CORS policy restricts origins explicitly to allowed frontends. Check for SQL injection or unvalidated inputs.

### 2. Frontend Audit (Angular 19 / TypeScript)
- **Standalone Architecture**: Confirm components use `standalone: true` without legacy `NgModule`.
- **Signal State Management**: Ensure state uses `signal()`, `computed()`, or `effect()`. Check that RxJS `BehaviorSubject` is replaced with Signals where appropriate.
- **Memory Leaks**: Verify any RxJS subscriptions use `takeUntilDestroyed()` or explicit cleanup.
- **Modern Control Flow**: Check templates use `@if`, `@for`, `@switch` instead of `*ngIf`, `*ngFor`.

### 3. Review Report Format

When reviewing code, generate a report structured as follows:

```markdown
# Code Review Report

## Executive Summary
[Brief overview of code quality and readiness for merge]

## Critical Issues (Must Fix)
- [ ] **[Component/File]**: Description of issue and concrete refactoring suggestion.

## Warnings & Improvements
- [ ] **[Component/File]**: Optimization or clean code suggestion.

## Positives & Compliance
- [Key architectural wins, e.g. clean Signal usage, proper Minimal API setup]
```
