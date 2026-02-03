# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction
- Create categories immediately after creation, even if no files are present.
- If a category is deleted, ensure it remains deleted upon the next startup and does not automatically restore the "Uncategorized" category; user deletions should be persistent.

## Code Style
- Use specific formatting rules
- Follow naming conventions
- Initialize `_filePath` in `TextViewerForm` to `string.Empty` to avoid compilation errors. Use `filePath ?? string.Empty` in the constructor and handle empty path cases during loading.
- Make custom controls (e.g., `ScrollingRichTextBox`) public and provide a parameterless constructor to ensure they display correctly in the designer. Avoid calling Win32 `SendMessage` during design time; `GetFirstVisibleLine` should check `rtb.IsHandleCreated`.

## Project-Specific Rules
- The project target framework is .NET Framework 4.7.2.
- Current open files include: `.github\copilot-instructions.md`, `Form1.cs`, and `Form1.Designer.cs`.
- Support hierarchical categorization (subcategories).
- Display the left line number column in `TextViewerForm` when opening text files (user preference). Use a double-buffered panel and `GetCharIndexFromPosition` for detecting the first visible line to avoid desynchronization between line numbers and text during scrolling.
- Split `TextViewerForm` into a designer file `TextViewerForm.Designer.cs` (containing control declarations and `InitializeComponent`) and a logic file `TextViewerForm.cs` (containing behavior, event handling, and loading logic).