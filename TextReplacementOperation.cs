using System;

namespace LayoutFixer;

internal sealed class TextReplacementOperation
{
    public Guid Id { get; } = Guid.NewGuid();
    public IntPtr TargetWindow { get; init; }
    public KeyboardLayoutInfo SourceLayout { get; set; } = default!;
    public KeyboardLayoutInfo TargetLayout { get; set; } = default!;
    public string SourceText { get; set; } = string.Empty;
    public string ConvertedText { get; set; } = string.Empty;
    public ClipboardSnapshot RestoreSnapshot { get; set; } = new();
    public string CopyTextHash { get; set; } = string.Empty;
    public int CopyTextLength { get; set; }
    public uint LastObservedClipboardSequence { get; set; }
    public uint RestoreSnapshotSequence { get; set; }
    public uint OurPasteClipboardSequence { get; set; }
}

