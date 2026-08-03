# Workspace Architectural & Agent Guidelines

## 1. General Principles
- Maintain clean separation of concerns between Backend (.NET 10 API) and Frontend (Angular 19 UI).
- Prioritize type safety, modern language features, and readable, self-documenting code.

## 2. Backend Rules (.NET 10 C#)
- Use **Minimal APIs** for endpoint mapping.
- Prefer **C# primary constructors** and **records** for DTOs.
- Use **Entity Framework Core** with async/await (`ToListAsync`, `FirstOrDefaultAsync`).
- Always configure CORS explicitly for Angular frontend (`http://localhost:4200`).
- Model validation should return standardized Problem Details or clear HTTP status codes (`200 OK`, `201 Created`, `400 BadRequest`, `404 NotFound`).

## 3. Frontend Rules (Angular 19)
- Use **Standalone Components** (no `NgModule` declarations).
- Use **Angular Signals** (`signal()`, `computed()`, `effect()`) for reactive state management instead of RxJS `BehaviorSubject` where possible.
- Use Modern Angular Control Flow (`@if`, `@else`, `@for`, `@switch`) in HTML templates.
- Use strongly-typed Reactive Forms (`FormBuilder`, `FormControl`).
- Keep components focused; extract API calls into dedicated injectable Services using `HttpClient`.

## 4. Code Review Agent Protocol
- When asked to perform a code review, activate the `.agents/skills/code-review/SKILL.md` skill.
- Inspect modified files across both backend (.NET 10) and frontend (Angular 19).
- Check for type safety, async/await correctness, memory leak prevention, and adherence to `AGENTS.md` guidelines.
- Output a structured review report highlighting Critical Issues, Warnings, and Architectural Victories.

