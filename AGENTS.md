# AGENTS.md — NexusClean AI Development & Spec Kit Guide

## 1. Project Overview

**NexusClean** is an intelligent, safety-first Windows disk cleanup and storage-relocation platform built with **C# / .NET 8**, **4-Layer Clean Architecture**, and **GitHub Spec Kit**. It resolves recurring full-disk (`C:`) issues caused by hidden caches, duplicate IDE extensions, auto-updaters, messaging media, WSL2 virtual disks, and Volume Shadow Copies without ever risking user credentials or OS stability.

## 2. Installed GitHub Spec Kit Skills (`.agents/skills/`)

This workspace is initialized with **GitHub Spec Kit (`specify-cli` — Antigravity `agy` integration)**. Use the following skills in order:

- `/speckit-constitution` — View or amend `.specify/memory/constitution.md` (Non-negotiable safety & Clean Architecture governance)
- `/speckit-specify` — Author or refine feature specifications in `specs/<feature>/spec.md`
- `/speckit-clarify` — Resolve underspecified requirements prior to planning
- `/speckit-plan` — Generate `plan.md`, `research.md`, `data-model.md`, `contracts/`, and `quickstart.md`
- `/speckit-checklist` — Generate "Unit Tests for Requirements" under `specs/<feature>/checklists/`
- `/speckit-tasks` — Generate dependency-ordered tasks in `specs/<feature>/tasks.md`
- `/speckit-analyze` — Run read-only cross-artifact consistency & coverage verification across `constitution.md`, `spec.md`, `plan.md`, and `tasks.md`
- `/speckit-implement` — Execute tasks from `tasks.md` phase by phase
- `/speckit-converge` — Audit codebase against specs and append remaining work as tasks

## 3. Non-Negotiable Safety & Architecture Rules

Before writing or modifying any code, every agent MUST obey `.specify/memory/constitution.md`:

1. **4-Layer Isolation**: `NexusClean.Domain` MUST remain 100% pure C# (zero NuGet packages, zero Win32 P/Invoke, zero filesystem mutations).
2. **Root Folder Lock (`RootSystemFolderGuard`)**: NEVER allow direct deletion of `%LOCALAPPDATA%`, `%APPDATA%`, `%USERPROFILE%`, `C:\Windows`, `C:\Program Files`, `C:\Program Files (x86)`, or drive roots.
3. **100% Browser Profile Preservation (`BrowserProfilePreservationGuard`)**: NEVER touch `Login Data`, `Cookies`, `Bookmarks`, `Preferences`, `Secure Preferences`, `Web Data`, `History`, `Extension Cookies`, or `Extensions` across Chrome, Edge, Brave, or Cốc Cốc.
4. **Relocate Over Blind Delete**: Continuously growing user stores (`ZaloData\media`, active WSL2 `ext4.vhdx`, `pagefile.sys`) MUST route to `SecondaryDriveRelocateStrategy` or isolated UAC workflows rather than bulk deletion.
5. **Sandbox-Only Automated Tests**: All xUnit tests MUST run inside `%TEMP%\NexusClean.Tests\<guid>` sandboxes—never against real host `AppData` or `C:\` paths.
