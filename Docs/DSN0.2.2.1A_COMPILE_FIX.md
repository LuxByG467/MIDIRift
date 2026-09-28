# DSN0.2.2.1a Compile Fix

Fixes CS0103 in `DsnPatchEditorPage.cs`.

Two `DisplayPromptAsync` calls accidentally used `initialValue=...` inside the
argument list. C# interpreted that as an assignment to a nonexistent local
identifier. They now use the correct named-argument syntax `initialValue: ...`.

The source tree was also scanned for additional `initialValue=` occurrences.
