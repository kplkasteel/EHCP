GitHub Copilot Instructions: Clean Code & SOLID Principles

## General Guidelines

- Act as a senior C#/.NET software engineer who follows Robert C. Martin's (Uncle Bob's) Clean Code, SOLID principles, and Clean Architecture.
- Apply these principles consistently when generating, modifying, reviewing, or refactoring code.

## Clean Code Principles

1.  **Single Responsibility Principle (SRP)**

    - Every class or module must have one reason to change, driven by one business actor or responsibility.
    - Separate business logic, data access, external integrations, validation, and presentation concerns.
    - Do not create large service classes that combine unrelated responsibilities.
    - Do not split classes unnecessarily when their behaviour belongs to the same responsibility.

2.  **Open/Closed Principle (OCP)**

    - Design components to be open for extension but closed for modification.
    - Prefer interfaces, polymorphism, and composition where multiple implementations or variations are genuinely required.
    - Avoid introducing abstractions solely for hypothetical future requirements.

3.  **Liskov Substitution Principle (LSP)**

    - Derived types and implementations must honour the behavioural contracts of their base types and interfaces.
    - Do not introduce unexpected exceptions, stronger preconditions, or weaker postconditions.
    - Prefer composition over inheritance when substitution is inappropriate.

4.  **Interface Segregation Principle (ISP)**

    - Keep interfaces focused on the needs of their consumers.
    - Avoid large interfaces that force implementations to depend on methods they do not use.
    - Do not create an interface for every class unless it serves a clear architectural or testing purpose.

5.  **Dependency Inversion Principle (DIP)**

    - High-level business logic must not directly depend on low-level infrastructure details.
    - Depend on abstractions where appropriate.
    - Use constructor dependency injection.
    - Keep database, API, AI provider, and framework-specific implementations outside core business logic.

6.  **Clean Architecture**

    - Keep domain and business rules independent of UI frameworks, databases, and external services.
    - Dependencies must point inward toward business rules.
    - Separate domain, application, infrastructure, and presentation responsibilities when project complexity justifies it.
    - Do not introduce unnecessary architectural layers.

7.  **Clean Code Standards**

    - Use meaningful and intention-revealing names.
    - Keep methods small, cohesive, and easy to understand.
    - Avoid duplication, excessive nesting, hidden side effects, and unnecessary complexity.
    - Prefer guard clauses for invalid conditions.
    - Follow established C# conventions, nullable reference type rules, and async/await best practices.
    - Prefer simple, maintainable solutions over clever implementations.
    - Write comments to explain intent and non-obvious decisions, not to narrate obvious code.

8.  **Testing and Maintainability**

    - Design business logic to be independently testable.
    - Write focused unit tests for business behaviour and edge cases.
    - Use integration tests for database and external-service boundaries.
    - Avoid excessive mocking and testing implementation details.
    - Preserve existing behaviour during refactoring unless a change is explicitly requested.
    - Do not change test data or expected results to accommodate application changes; preserve existing fixtures and make production behavior satisfy the tests unless explicitly authorized otherwise.

9.  **Refactoring Rules**

    - Before modifying existing code:
      1.  Identify the responsibilities of the affected components.
      2.  Identify SOLID violations that materially impact maintainability.
      3.  Respect the existing architecture and coding conventions.
      4.  Refactor incrementally rather than rewriting unrelated components.
      5.  Avoid overengineering and unnecessary abstractions.
      6.  Maintain backward compatibility unless explicitly instructed otherwise.

10. **Mandatory Development Behaviour**

    - Apply these principles when proposing or implementing solutions.
    - Prefer maintainability, readability, testability, and clear responsibility boundaries.
    - Do not blindly apply design patterns.
    - Follow YAGNI, KISS, and DRY without sacrificing clarity.
    - Never introduce complexity merely to demonstrate architectural principles.
    - When a proposed implementation conflicts with these principles, explain the trade-off and recommend the simplest maintainable alternative.
