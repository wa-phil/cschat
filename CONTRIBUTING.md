# Contributing to CSChat

Thank you for your interest in contributing to **CSChat**, our open-source C#-based chatbot project!

We welcome contributions of all kinds—bug reports, code improvements, new features, documentation, tests, and more. This document outlines how to contribute effectively and respectfully.

---

## 📋 Table of Contents

1. [Code of Conduct](#code-of-conduct)
2. [Getting Started](#getting-started)
3. [Project Structure](#project-structure)
4. [Development Workflow](#development-workflow)
5. [Coding Guidelines](#coding-guidelines)
6. [Testing](#testing)
7. [Submitting Changes](#submitting-changes)
8. [Feature Requests & Issues](#feature-requests--issues)
9. [License](#license)

---

## ✅ Code of Conduct

We follow a [Contributor Covenant](https://www.contributor-covenant.org/) Code of Conduct. Be respectful, constructive, and inclusive. We want a healthy, collaborative environment for everyone.

---

## 🚀 Getting Started

To start contributing:

1. **Fork** the repository
2. **Clone** your fork locally:

   ```bash
   git clone https://github.com/wa-phil/cschat.git
   cd cschat
   ```
3. Install [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
4. Build the project:

   ```bash
   dotnet build
   ```

---

## 🗂 Project Structure

```
/cschat           - Core chatbot console app
/engine           - Chat engine interfaces and provider infrastructure
/providers        - LLM providers (e.g., Ollama, AzureAI)
/unittests        - xUnit-based unit tests
```

---

## 🔁 Development Workflow

* Create a branch from `main`

  ```bash
  git checkout -b feature/my-new-feature
  ```
* Make your changes
* Run tests (`dotnet test`)
* Commit with a meaningful message:

  ```
  feat(tooling): add new command-line option for model selection
  ```
* Push your branch and open a **Pull Request**

> ✨ PRs should describe the problem, the solution, and any edge cases.

---

## 💻 Coding Guidelines

* Follow standard **C# coding conventions**
* Use `async`/`await` consistently
* Prefer dependency injection over static or global state
* Avoid unnecessary third-party dependencies
* Keep feature logic testable and modular
* Add XML comments for public interfaces and methods
* If modifying prompt logic, ensure it degrades gracefully across providers

---

## ✅ Testing

* Add or update **unit tests** for any code you change
* Run `dotnet test` before committing
* For major changes, include **integration tests** where appropriate

---

## 📆 Submitting Changes

When opening a pull request:

* Ensure your branch is up to date with `main`
* Confirm it builds and passes tests
* Clearly explain the purpose of your change
* Tag your PR with labels (`bug`, `enhancement`, `question`, etc.) if possible

---

## 💡 Feature Requests & Issues

Have an idea? Found a bug?

* Open a [GitHub Issue](https://github.com/YOUR-ORG-NAME/cschat/issues)
* Provide detailed steps to reproduce bugs
* Suggest specific improvements or use cases for new features

---

## 📜 License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).

---

We appreciate your time and effort. Happy hacking!
— *The CSChat Team*
